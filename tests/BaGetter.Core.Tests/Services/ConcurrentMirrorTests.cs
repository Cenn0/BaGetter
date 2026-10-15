using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGetter.Core.Tests.Services;

public class ConcurrentMirrorTests
{
    private const string PackageId = "Example.Package";
    private static readonly NuGetVersion Version = NuGetVersion.Parse("1.2.3");

    [Fact]
    public async Task ConcurrentColdCacheRequestsDownloadAndIndexOnce()
    {
        var packages = new Mock<IPackageDatabase>();
        var upstream = new Mock<IUpstreamClient>();
        var indexer = new Mock<IPackageIndexingService>();
        var indexed = 0;
        var downloads = 0;

        packages.Setup(p => p.ExistsAsync(PackageId, Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Volatile.Read(ref indexed) == 1);
        upstream.Setup(u => u.GetServiceIndexUrl())
            .Returns("https://api.nuget.org/v3/index.json");
        upstream.Setup(u => u.DownloadPackageOrNullAsync(PackageId, Version, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref downloads);
                await Task.Delay(25);
                return (Stream)new MemoryStream([1, 2, 3]);
            });
        indexer.Setup(i => i.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(25);
                Volatile.Write(ref indexed, 1);
                return PackageIndexingResult.Success;
            });

        var service = new PackageService(
            packages.Object, upstream.Object, indexer.Object,
            Mock.Of<ILogger<PackageService>>());

        var requests = Enumerable.Range(0, 32).Select(_ =>
            service.ExistsAsync(PackageId, Version, CancellationToken.None));
        var results = await Task.WhenAll(requests);

        Assert.All(results, result => Assert.True(result));
        Assert.Equal(1, downloads);
        indexer.Verify(i => i.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PackageAlreadyExistsAfterCompetingPublishIsNotReportedMissing()
    {
        var packages = new Mock<IPackageDatabase>();
        var upstream = new Mock<IUpstreamClient>();
        var indexer = new Mock<IPackageIndexingService>();
        var checks = 0;

        packages.Setup(p => p.ExistsAsync(PackageId, Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref checks) >= 3);
        upstream.Setup(u => u.GetServiceIndexUrl())
            .Returns("https://example.org/v3/index.json");
        upstream.Setup(u => u.DownloadPackageOrNullAsync(PackageId, Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2, 3]) as Stream);
        indexer.Setup(i => i.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackageIndexingResult.PackageAlreadyExists);

        var service = new PackageService(
            packages.Object, upstream.Object, indexer.Object,
            Mock.Of<ILogger<PackageService>>());

        Assert.True(await service.ExistsAsync(PackageId, Version, CancellationToken.None));
    }
}
