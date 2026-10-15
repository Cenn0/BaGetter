using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGetter.Core.Tests.Services;

public class RetentionKeepsCurrentPackageTests
{
    [Fact]
    public async Task OldMirroredVersionIsNotDeletedDuringItsIndexing()
    {
        var oldVersion = new NuGetVersion("1.0.0");
        var newVersion = new NuGetVersion("6.0.0");
        var current = new Package { Id = "Example.Package", Version = oldVersion };
        var packages = new Mock<IPackageDatabase>();
        var storage = new Mock<IPackageStorageService>();

        packages.Setup(p => p.FindAsync(current.Id, true, CancellationToken.None))
            .ReturnsAsync(new List<Package>
            {
                current,
                new Package { Id = current.Id, Version = newVersion }
            });
        var settings = new Mock<IOptionsSnapshot<BaGetterOptions>>();
        settings.Setup(o => o.Value).Returns(new BaGetterOptions());
        var service = new PackageDeletionService(
            packages.Object, storage.Object, settings.Object,
            Mock.Of<ILogger<PackageDeletionService>>());

        var deleted = await service.DeleteOldVersionsAsync(
            current, maxMajor: 1, maxMinor: null, maxPatch: null,
            maxPrerelease: null, CancellationToken.None);

        Assert.Equal(0, deleted);
        packages.Verify(p => p.HardDeletePackageAsync(
            current.Id, oldVersion, It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(s => s.DeleteAsync(
            current.Id, oldVersion, It.IsAny<CancellationToken>()), Times.Never);
    }
}
