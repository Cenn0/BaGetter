using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Protocol.Models;
using NuGet.Versioning;

namespace BaGetter.Core;

/// <summary>
/// Implements the NuGet Package Content resource in NuGet's V3 protocol.
/// </summary>
public class DefaultPackageContentService : IPackageContentService
{
    private readonly IPackageService _packages;
    private readonly IPackageStorageService _storage;

    public DefaultPackageContentService(
        IPackageService packages,
        IPackageStorageService storage)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<PackageVersionsResponse> GetPackageVersionsOrNullAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var versions = await _packages.FindPackageVersionsAsync(id, cancellationToken);
        if (!versions.Any())
        {
            return null;
        }

        return new PackageVersionsResponse
        {
            Versions = versions
                .Select(v => v.ToNormalizedString())
                .Select(v => v.ToLowerInvariant())
                .ToList()
        };
    }

    public async Task<Stream> GetPackageContentStreamOrNullAsync(
        string id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        var stream = await GetWithMirrorRepairAsync(
            id, version, _storage.GetPackageStreamAsync, cancellationToken);
        if (stream != null)
            await _packages.AddDownloadAsync(id, version, cancellationToken);
        return stream;
    }

    public Task<Stream> GetPackageManifestStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
        => GetWithMirrorRepairAsync(id, version, _storage.GetNuspecStreamAsync, cancellationToken);

    public async Task<Stream> GetPackageReadmeStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        var package = await _packages.FindPackageOrNullAsync(id, version, cancellationToken);
        return package == null || !package.HasReadme ? null :
            await GetWithMirrorRepairAsync(id, version, _storage.GetReadmeStreamAsync, cancellationToken);
    }

    public async Task<Stream> GetPackageIconStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        var package = await _packages.FindPackageOrNullAsync(id, version, cancellationToken);
        return package == null || !package.HasEmbeddedIcon ? null :
            await GetWithMirrorRepairAsync(id, version, _storage.GetIconStreamAsync, cancellationToken);
    }

    private async Task<Stream> GetWithMirrorRepairAsync(
        string id, NuGetVersion version,
        Func<Package, CancellationToken, Task<Stream>> reader,
        CancellationToken cancellationToken)
    {
        var package = await _packages.FindPackageOrNullAsync(id, version, cancellationToken);
        if (package == null) return null;

        try
        {
            return await reader(package, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            if (!await _packages.RepairMirroredPackageAsync(id, version, cancellationToken)) throw;
        }
        catch (DirectoryNotFoundException)
        {
            if (!await _packages.RepairMirroredPackageAsync(id, version, cancellationToken)) throw;
        }

        // The repair switched the committed pointer; discard the stale row.
        package = await _packages.FindPackageOrNullAsync(id, version, cancellationToken);
        return package == null ? null : await reader(package, cancellationToken);
    }
}
