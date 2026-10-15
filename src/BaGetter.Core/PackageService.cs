using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGetter.Core;

public class PackageService : IPackageService
{
    private readonly IPackageDatabase _db;
    private readonly IUpstreamClient _upstream;
    private readonly IPackageIndexingService _indexer;
    private readonly ILogger<PackageService> _logger;

    // Bounded process-wide gates: this service is registered as transient.
    // Distinct package/version pairs are striped across these gates.
    private static readonly SemaphoreSlim[] MirrorLocks = Enumerable.Range(0, 256)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public PackageService(
        IPackageDatabase db,
        IUpstreamClient upstream,
        IPackageIndexingService indexer,
        ILogger<PackageService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsAsync(
        string id,
        CancellationToken cancellationToken)
    {
        // Query local data first so an upstream outage cannot break access to
        // versions already cached by this server.
        var localPackages = await _db.FindAsync(id, includeUnlisted: true, cancellationToken);
        IReadOnlyList<NuGetVersion> upstreamVersions;
        try
        {
            upstreamVersions = await _upstream.ListPackageVersionsAsync(id, cancellationToken);
        }
        catch (UpstreamUnavailableException) when (localPackages.Count > 0)
        {
            _logger.LogWarning("Serving cached versions of {PackageId} because upstream is unavailable", id);
            return localPackages.Select(p => p.Version).ToList();
        }

        // Merge the local package versions into the upstream package versions.
        var localVersions = localPackages.Select(p => p.Version);

        if (!upstreamVersions.Any()) return localVersions.ToList();
        if (!localPackages.Any()) return upstreamVersions;

        return upstreamVersions.Concat(localVersions).Distinct().ToList();
    }

    public async Task<IReadOnlyList<Package>> FindPackagesAsync(string id, CancellationToken cancellationToken)
    {
        var localPackages = await _db.FindAsync(id, includeUnlisted: true, cancellationToken);
        IReadOnlyList<Package> upstreamPackages;
        try
        {
            upstreamPackages = await _upstream.ListPackagesAsync(id, cancellationToken);
        }
        catch (UpstreamUnavailableException) when (localPackages.Count > 0)
        {
            _logger.LogWarning("Serving cached metadata for {PackageId} because upstream is unavailable", id);
            return localPackages;
        }

        if (!upstreamPackages.Any()) return localPackages;
        if (!localPackages.Any()) return upstreamPackages;

        // Merge the local packages into the upstream packages.
        var result = upstreamPackages.ToDictionary(p => p.Version);
        var local = localPackages.ToDictionary(p => p.Version);

        foreach (var localPackage in local)
        {
            result[localPackage.Key] = localPackage.Value;
        }

        return result.Values.ToList();
    }

    public async Task<Package> FindPackageOrNullAsync(
        string id,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        if (!await MirrorAsync(id, version, cancellationToken))
        {
            return null;
        }

        return await _db.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await MirrorAsync(id, version, cancellationToken);
    }

    public async Task AddDownloadAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        await _db.AddDownloadAsync(packageId, version, cancellationToken);
    }

    /// <summary>
    /// Index the package from an upstream if it does not exist locally.
    /// </summary>
    /// <param name="id">The package ID to index from an upstream.</param>
    /// <param name="version">The package version to index from an upstream.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>True if the package exists locally or was indexed from an upstream source.</returns>
    private async Task<bool> MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (await _db.ExistsAsync(id, version, cancellationToken))
        {
            return true;
        }

        // Multiple concurrent requests can observe the same cold cache.
        // Use the same gate for case-insensitive package IDs and versions.
        var lockKey = $"{id.ToLowerInvariant()}/{version.ToNormalizedString().ToLowerInvariant()}";
        var lockIndex = (StringComparer.Ordinal.GetHashCode(lockKey) & int.MaxValue) % MirrorLocks.Length;
        var gate = MirrorLocks[lockIndex];

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (await _db.ExistsAsync(id, version, cancellationToken))
            {
                return true;
            }

            return await MirrorUncachedAsync(id, version, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> MirrorUncachedAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        var cacheFeedUrl = _upstream.GetServiceIndexUrl();

        _logger.LogInformation(
            "Package {PackageId} {PackageVersion} does not exist locally. Checking upstream feed ({cacheFeedUrl})...",
            id,
            version,
            cacheFeedUrl);

        try
        {
            using var packageStream = await _upstream.DownloadPackageOrNullAsync(id, version, cancellationToken);
            if (packageStream == null)
            {
                _logger.LogWarning(
                    "Upstream feed does not have package {PackageId} {PackageVersion}",
                    id,
                    version);
                return false;
            }

            _logger.LogInformation(
                "Downloaded package {PackageId} {PackageVersion}, indexing...",
                id,
                version);

            var result = await _indexer.IndexAsync(packageStream, cacheFeedUrl, cancellationToken);

            _logger.LogInformation(
                "Finished indexing package {PackageId} {PackageVersion} from upstream feed with result {Result}",
                id,
                version,
                result);

            if (result == PackageIndexingResult.Success)
            {
                return true;
            }

            if (result == PackageIndexingResult.PackageAlreadyExists &&
                await _db.ExistsAsync(id, version, cancellationToken))
            {
                return true;
            }

            // A corrupted/invalid upstream artifact must not be advertised
            // as a normal missing version, and must never poison the cache.
            throw new UpstreamUnavailableException(
                $"Upstream package {id} {version} could not be indexed ({result})",
                new InvalidDataException($"Package indexing result: {result}"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Failed to index package {PackageId} {PackageVersion} from upstream",
                id,
                version);

            // Another replica may have indexed the package while this failed.
            if (await _db.ExistsAsync(id, version, cancellationToken))
            {
                return true;
            }

            // An upstream or storage error is not a confirmed package miss.
            throw new UpstreamUnavailableException(
                $"Unable to fetch or index {id} {version} from upstream", e);
        }
    }
}
