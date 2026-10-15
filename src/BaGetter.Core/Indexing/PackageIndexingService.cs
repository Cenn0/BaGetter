using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;

namespace BaGetter.Core;

public class PackageIndexingService : IPackageIndexingService
{
    private readonly IPackageDatabase _packages;
    private readonly IPackageStorageService _storage;
    private readonly ISearchIndexer _search;
    private readonly SystemTime _time;
    private readonly IOptionsSnapshot<BaGetterOptions> _options;
    private readonly IOptionsSnapshot<RetentionOptions> _retentionOptions;
    private readonly ILogger<PackageIndexingService> _logger;
    private readonly IPackageDeletionService _packageDeletionService;

    public PackageIndexingService(
        IPackageDatabase packages,
        IPackageStorageService storage,
        IPackageDeletionService packageDeletionService,
        ISearchIndexer search,
        SystemTime time,
        IOptionsSnapshot<BaGetterOptions> options,
        IOptionsSnapshot<RetentionOptions> retentionOptions,
        ILogger<PackageIndexingService> logger)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _retentionOptions = retentionOptions ?? throw new ArgumentNullException(nameof(retentionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _packageDeletionService = packageDeletionService ?? throw new ArgumentNullException(nameof(packageDeletionService));
#pragma warning disable CS0618 // Type or member is obsolete
        if (_options.Value.MaxVersionsPerPackage > 0)
        {
            _logger.LogError("MaxVersionsPerPackage is deprecated and is not used. Please use MaxMajorVersions, MaxMinorVersions, MaxPatchVersions, and MaxPrereleaseVersions instead.");
        }
#pragma warning restore CS0618 // Type or member is obsolete
    }

    public Task<PackageIndexingResult> IndexAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return IndexAsync(stream, null, cancellationToken);
    }

    public async Task<PackageIndexingResult> IndexAsync(Stream packageStream, string cacheFeedUrl = null, CancellationToken cancellationToken = default)
    {
        // Try to extract all the necessary information from the package.
        Package package;
        Stream nuspecStream;
        Stream readmeStream;
        Stream iconStream;

        try
        {
            using var packageReader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
            // Validate the complete archive before publishing any bytes or metadata.
            // The old path checked the nuspec but did not validate archive entries.
            await packageReader.ValidatePackageEntriesAsync(cancellationToken);
            package = packageReader.GetPackageMetadata();
            package.CachedFrom = cacheFeedUrl;
            package.Published = _time.UtcNow;

            nuspecStream = await packageReader.GetNuspecAsync(cancellationToken);
            nuspecStream = await nuspecStream.AsTemporaryFileStreamAsync(cancellationToken);

            if (package.HasReadme)
            {
                readmeStream = await packageReader.GetReadmeAsync(cancellationToken);
                readmeStream = await readmeStream.AsTemporaryFileStreamAsync(cancellationToken);
            }
            else
            {
                readmeStream = null;
            }

            if (package.HasEmbeddedIcon)
            {
                iconStream = await packageReader.GetIconAsync(cancellationToken);
                iconStream = await iconStream.AsTemporaryFileStreamAsync(cancellationToken);
            }
            else
            {
                iconStream = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Package archive validation failed");

            return PackageIndexingResult.InvalidPackage;
        }

        // Every upload gets a private immutable revision. The database row is
        // the sole publication point: the previous revision stays readable.
        var replaceExisting = await _packages.ExistsAsync(package.Id, package.Version, cancellationToken);
        if (replaceExisting)
        {
            var existing = await _packages.FindOrNullAsync(package.Id, package.Version, true, cancellationToken);
            var repairingMirror = cacheFeedUrl != null &&
                string.Equals(existing?.CachedFrom, cacheFeedUrl, StringComparison.OrdinalIgnoreCase);
            if (!repairingMirror &&
                (_options.Value.AllowPackageOverwrites == PackageOverwriteAllowed.False ||
                (_options.Value.AllowPackageOverwrites == PackageOverwriteAllowed.PrereleaseOnly && !package.IsPrerelease)))
            {
                return PackageIndexingResult.PackageAlreadyExists;
            }
        }

        package.StorageRevision = Guid.NewGuid().ToString("N");

        // Archive entry integrity was validated above, prior to publication.
        _logger.LogInformation(
            "Validated package {PackageId} {PackageVersion}, persisting content to storage...",
            package.Id,
            package.NormalizedVersionString);

        PackageAddResult result;
        try
        {
            packageStream.Position = 0;
            await _storage.SavePackageContentAsync(
                package, packageStream, nuspecStream, readmeStream, iconStream, cancellationToken);

            if (replaceExisting)
            {
                // Atomic SQL transaction or Azure Table entity replacement.
                await _packages.ReplaceAsync(package, cancellationToken);
                result = PackageAddResult.Success;
            }
            else
            {
                result = await _packages.AddAsync(package, cancellationToken);
            }
            if (result == PackageAddResult.PackageAlreadyExists)
            {
                await CleanupUnpublishedRevisionAsync(package);
                return PackageIndexingResult.PackageAlreadyExists;
            }
            if (result != PackageAddResult.Success)
                throw new InvalidOperationException($"Unknown {nameof(PackageAddResult)} value: {result}");
        }
        catch
        {
            await CleanupUnpublishedRevisionAsync(package);
            throw;
        }

        _logger.LogInformation(
            "Successfully persisted package {Id} {Version} metadata to database. Indexing in search...",
            package.Id,
            package.NormalizedVersionString);

        await _search.IndexAsync(package, cancellationToken);

        if (_retentionOptions.Value.MaxMajorVersions.HasValue ||
            _retentionOptions.Value.MaxMinorVersions.HasValue ||
            _retentionOptions.Value.MaxPatchVersions.HasValue ||
            _retentionOptions.Value.MaxPrereleaseVersions.HasValue)
        {
            try
            {
                _logger.LogInformation(
                    "Deleting older packages for package {PackageId} {PackageVersion}",
                    package.Id,
                    package.NormalizedVersionString);

                var deleted = await _packageDeletionService.DeleteOldVersionsAsync(
                    package,
                    _retentionOptions.Value.MaxMajorVersions,
                    _retentionOptions.Value.MaxMinorVersions,
                    _retentionOptions.Value.MaxPatchVersions,
                    _retentionOptions.Value.MaxPrereleaseVersions,
                    cancellationToken);
                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "Deleted {packages} older packages for package {PackageId} {PackageVersion}",
                        deleted,
                        package.Id,
                        package.NormalizedVersionString);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "Failed to cleanup older versions of package {PackageId} {PackageVersion}",
                    package.Id,
                    package.NormalizedVersionString);
            }
        }

        _logger.LogInformation(
            "Successfully indexed package {Id} {Version} in search",
            package.Id,
            package.NormalizedVersionString);

        return PackageIndexingResult.Success;
    }

    private async Task CleanupUnpublishedRevisionAsync(Package package)
    {
        // A failure can follow a successful commit with a lost acknowledgement.
        // Never delete the revision if it is now referenced by the database.
        // Database lookup errors intentionally leave content for later GC.
        try
        {
            var committed = await _packages.FindOrNullAsync(
                package.Id, package.Version, includeUnlisted: true, CancellationToken.None);
            if (committed?.StorageRevision == package.StorageRevision) return;
            await _storage.DeleteAsync(package, CancellationToken.None);
        }
        catch (Exception error)
        {
            _logger.LogError(error,
                "Could not clean incomplete package revision {Revision} for {Id} {Version}",
                package.StorageRevision, package.Id, package.NormalizedVersionString);
        }
    }
}
