using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Protocol;
using BaGetter.Protocol.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace BaGetter.Core;

/// <summary>
/// The mirroring client for a NuGet server that uses the V3 protocol.
/// </summary>
public class V3UpstreamClient : IUpstreamClient
{
    private readonly NuGetClient _client;
    private readonly ILogger<V3UpstreamClient> _logger;
    private readonly UpstreamMetadataCache _metadataCache;
    private readonly TimeSpan _metadataTtl;
    private static readonly char[] AuthorSeparator = [',', ';', '\t', '\n', '\r'];
    private static readonly char[] TagSeparator = [' '];

    // Preserve the existing constructor for embedding scenarios.
    public V3UpstreamClient(NuGetClient client, ILogger<V3UpstreamClient> logger)
        : this(client, logger, new UpstreamMetadataCache(), Options.Create(new MirrorOptions { MetadataCacheSeconds = 0 }))
    {
    }

    public V3UpstreamClient(
        NuGetClient client,
        ILogger<V3UpstreamClient> logger,
        UpstreamMetadataCache metadataCache,
        IOptions<MirrorOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
        _metadataTtl = TimeSpan.FromSeconds((options ?? throw new ArgumentNullException(nameof(options))).Value.MetadataCacheSeconds);
    }
    public string GetServiceIndexUrl() => _client.ServiceIndexUrl;

    public async Task<Stream> DownloadPackageOrNullAsync(
        string id,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        try
        {
            using var downloadStream = await _client.DownloadPackageAsync(id, version, cancellationToken);
            return await downloadStream.AsTemporaryFileStreamAsync(cancellationToken);
        }
        catch (PackageNotFoundException)
        {
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to download {PackageId} {PackageVersion} from upstream", id, version);
            throw new UpstreamUnavailableException($"Upstream failed to download {id} {version}", e);
        }
    }

    public async Task<IReadOnlyList<Package>> ListPackagesAsync(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _metadataCache.GetOrAddAsync(
                $"v3:packages:{_client.ServiceIndexUrl}:{id.ToLowerInvariant()}",
                _metadataTtl,
                async () => (IReadOnlyList<Package>)(await _client.GetPackageMetadataAsync(id, cancellationToken))
                    .Select(ToPackage).ToArray(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to mirror {PackageId}'s upstream metadata", id);
            throw new UpstreamUnavailableException($"Upstream metadata unavailable for {id}", e);
        }
    }

    public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _metadataCache.GetOrAddAsync(
                $"v3:versions:{_client.ServiceIndexUrl}:{id.ToLowerInvariant()}",
                _metadataTtl,
                () => _client.ListPackageVersionsAsync(id, includeUnlisted: true, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to mirror {PackageId}'s upstream versions", id);
            throw new UpstreamUnavailableException($"Upstream versions unavailable for {id}", e);
        }
    }

    private Package ToPackage(PackageMetadata metadata)
    {
        var version = metadata.ParseVersion();

        return new Package
        {
            Id = metadata.PackageId,
            Version = version,
            Authors = ParseAuthors(metadata.Authors),
            Description = metadata.Description,
            Downloads = 0,
            HasReadme = false,
            IsPrerelease = version.IsPrerelease,
            Language = metadata.Language,
            Listed = metadata.IsListed(),
            MinClientVersion = metadata.MinClientVersion,
            Published = metadata.Published.UtcDateTime,
            RequireLicenseAcceptance = metadata.RequireLicenseAcceptance,
            Summary = metadata.Summary,
            Title = metadata.Title,
            IconUrl = ParseUri(metadata.IconUrl),
            LicenseUrl = ParseUri(metadata.LicenseUrl),
            ProjectUrl = ParseUri(metadata.ProjectUrl),
            PackageTypes = new List<PackageType>(),
            RepositoryUrl = null,
            RepositoryType = null,
            SemVerLevel = version.IsSemVer2 ? SemVerLevel.SemVer2 : SemVerLevel.Unknown,
            Tags = ParseTags(metadata.Tags),

            Dependencies = ToDependencies(metadata)
        };
    }

    private static Uri ParseUri(string uriString)
    {
        if (uriString == null) return null;

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri;
    }

    private static string[] ParseAuthors(string authors)
    {
        if (string.IsNullOrEmpty(authors))
        {
            return Array.Empty<string>();
        }

        return authors.Split(AuthorSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string[] ParseTags(IEnumerable<string> tags)
    {
        if (tags is null)
        {
            return Array.Empty<string>();
        }

        return tags
            .SelectMany(t => t.Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
    }

    private List<PackageDependency> ToDependencies(PackageMetadata package)
    {
        if ((package.DependencyGroups?.Count ?? 0) == 0)
        {
            return new List<PackageDependency>();
        }

        return package.DependencyGroups
            .SelectMany(ToDependencies)
            .ToList();
    }

    private IEnumerable<PackageDependency> ToDependencies(DependencyGroupItem group)
    {
        // BaGetter stores a dependency group with no dependencies as a package dependency
        // with no package id nor package version.
        if ((group.Dependencies?.Count ?? 0) == 0)
        {
            return new[]
            {
                new PackageDependency
                {
                    Id = null,
                    VersionRange = null,
                    TargetFramework = group.TargetFramework,
                }
            };
        }

        return group.Dependencies.Select(d => new PackageDependency
        {
            Id = d.Id,
            VersionRange = d.Range,
            TargetFramework = group.TargetFramework,
        });
    }
}
