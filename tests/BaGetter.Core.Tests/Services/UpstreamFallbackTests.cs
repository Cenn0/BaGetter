using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGetter.Core.Tests.Services;

public class UpstreamFallbackTests
{
    private const string PackageId = "Example";
    private static readonly NuGetVersion Version = NuGetVersion.Parse("1.0.0");

    [Fact]
    public async Task CachedVersionsSurviveUpstreamFailure()
    {
        var db = new Mock<IPackageDatabase>();
        var upstream = new Mock<IUpstreamClient>();
        db.Setup(x => x.FindAsync(PackageId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Package> { new Package { Id = PackageId, Version = Version } });
        upstream.Setup(x => x.ListPackageVersionsAsync(PackageId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UpstreamUnavailableException("unreachable", new IOException()));

        var service = Make(db, upstream);
        var versions = await service.FindPackageVersionsAsync(PackageId, CancellationToken.None);
        Assert.Equal(Version, Assert.Single(versions));
    }

    [Fact]
    public async Task CachedMetadataSurvivesUpstreamFailure()
    {
        var db = new Mock<IPackageDatabase>();
        var upstream = new Mock<IUpstreamClient>();
        db.Setup(x => x.FindAsync(PackageId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Package> { new Package { Id = PackageId, Version = Version } });
        upstream.Setup(x => x.ListPackagesAsync(PackageId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UpstreamUnavailableException("unreachable", new IOException()));

        var service = Make(db, upstream);
        var packages = await service.FindPackagesAsync(PackageId, CancellationToken.None);
        Assert.Equal(Version, Assert.Single(packages).Version);
    }

    [Fact]
    public async Task UncachedVersionsDoNotConvertOutageIntoNotFound()
    {
        var db = new Mock<IPackageDatabase>();
        var upstream = new Mock<IUpstreamClient>();
        db.Setup(x => x.FindAsync(PackageId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Package>());
        upstream.Setup(x => x.ListPackageVersionsAsync(PackageId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UpstreamUnavailableException("unreachable", new IOException()));

        await Assert.ThrowsAsync<UpstreamUnavailableException>(() =>
            Make(db, upstream).FindPackageVersionsAsync(PackageId, CancellationToken.None));
    }

    private static PackageService Make(Mock<IPackageDatabase> db, Mock<IUpstreamClient> upstream) =>
        new PackageService(db.Object, upstream.Object, Mock.Of<IPackageIndexingService>(),
            NullLogger<PackageService>.Instance);
}
