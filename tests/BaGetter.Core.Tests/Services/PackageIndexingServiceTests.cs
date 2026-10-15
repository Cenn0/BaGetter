using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Packaging;
using NuGet.Versioning;
using Xunit;

namespace BaGetter.Core.Tests.Services;

public class PackageIndexingServiceTests
{
    private readonly Mock<IPackageDatabase> _packages;
    private readonly Mock<IPackageStorageService> _storage;
    private readonly Mock<ISearchIndexer> _search;
    private readonly Mock<SystemTime> _time;
    private readonly Mock<IPackageDeletionService> _deleter;
    private readonly PackageIndexingService _target;
    private readonly BaGetterOptions _mockOptions;
    private RetentionOptions _retentionOptions;

    public PackageIndexingServiceTests()
    {
        _packages = new Mock<IPackageDatabase>(MockBehavior.Strict);
        _storage = new Mock<IPackageStorageService>(MockBehavior.Strict);
        _search = new Mock<ISearchIndexer>(MockBehavior.Strict);
        _time = new Mock<SystemTime>(MockBehavior.Loose);
        _deleter = new Mock<IPackageDeletionService>(MockBehavior.Strict);
        _mockOptions = new();
        _retentionOptions = new();
        var options = new Mock<IOptionsSnapshot<BaGetterOptions>>(MockBehavior.Strict);
        options.Setup(o => o.Value).Returns(_mockOptions);
        var retentionOptions = new Mock<IOptionsSnapshot<RetentionOptions>>(MockBehavior.Strict);
        retentionOptions.Setup(o => o.Value).Returns(_retentionOptions);

        _target = new PackageIndexingService(
            _packages.Object,
            _storage.Object,
            _deleter.Object,
            _search.Object,
            _time.Object,
            options.Object,
            retentionOptions.Object,
            Mock.Of<ILogger<PackageIndexingService>>());
    }

    // TODO: Add malformed package tests

    [Fact]
    public async Task IndexAsync_WhenPackageAlreadyExists_AndOverwriteForbidden_ReturnsPackageAlreadyExists()
    {
        // Arrange
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.False;

        var builder = new PackageBuilder
        {
            Id = "bagetter-test",
            Version = NuGetVersion.Parse("1.0.0"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        var assemblyFile = GetType().Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(builder.Id, builder.Version, default)).ReturnsAsync(true);
        _packages.Setup(p => p.FindOrNullAsync(builder.Id, builder.Version, true, default))
            .ReturnsAsync(new Package { Id = builder.Id, Version = builder.Version });

        // Act
        var result = await _target.IndexAsync(stream, default);

        // Assert
        Assert.Equal(PackageIndexingResult.PackageAlreadyExists, result);
    }

    [Fact]
    public async Task IndexAsync_WhenPackageAlreadyExists_AndOverwriteAllowed_IndexesPackage()
    {
        // Arrange
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.True;

        var builder = new PackageBuilder
        {
            Id = "bagetter-test",
            Version = NuGetVersion.Parse("1.0.0"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        var assemblyFile = GetType().Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(builder.Id, builder.Version, default)).ReturnsAsync(true);
        _packages.Setup(p => p.FindOrNullAsync(builder.Id, builder.Version, true, default))
            .ReturnsAsync(new Package { Id = builder.Id, Version = builder.Version });
        _packages.Setup(p => p.ReplaceAsync(It.Is<Package>(p1 => p1.Id == builder.Id && p1.Version.ToString() == builder.Version.ToString()), default))
            .Returns(Task.CompletedTask);
        _storage.Setup(s => s.SavePackageContentAsync(It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), stream, It.IsAny<FileStream>(), default, default, default)).Returns(Task.CompletedTask);

        _search.Setup(s => s.IndexAsync(It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), default)).Returns(Task.CompletedTask);

        // Act
        var result = await _target.IndexAsync(stream, default);

        // Assert
        Assert.Equal(PackageIndexingResult.Success, result);
    }

    [Fact]
    public async Task IndexAsync_WhenPrereleasePackageAlreadyExists_AndOverwritePrereleaseAllowed_IndexesPackage()
    {
        // Arrange
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.PrereleaseOnly;

        var builder = new PackageBuilder
        {
            Id = "bagetter-test",
            Version = NuGetVersion.Parse("1.0.0-beta"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        var assemblyFile = GetType().Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(builder.Id, builder.Version, default)).ReturnsAsync(true);
        _packages.Setup(p => p.FindOrNullAsync(builder.Id, builder.Version, true, default))
            .ReturnsAsync(new Package { Id = builder.Id, Version = builder.Version });
        _packages.Setup(p => p.ReplaceAsync(It.Is<Package>(p1 => p1.Id == builder.Id && p1.Version.ToString() == builder.Version.ToString()), default))
            .Returns(Task.CompletedTask);
        _storage.Setup(s => s.SavePackageContentAsync(It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), stream, It.IsAny<FileStream>(), default, default, default)).Returns(Task.CompletedTask);

        _search.Setup(s => s.IndexAsync(It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), default)).Returns(Task.CompletedTask);

        // Act
        var result = await _target.IndexAsync(stream, default);

        // Assert
        Assert.Equal(PackageIndexingResult.Success, result);
    }

    [Fact]
    public async Task IndexAsync_WhenPrereleasePackageAlreadyExists_AndOverwriteForbidden_ReturnsPackageAlreadyExists()
    {
        // Arrange
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.False;

        var builder = new PackageBuilder
        {
            Id = "bagetter-test",
            Version = NuGetVersion.Parse("1.0.0-beta"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        var assemblyFile = GetType().Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(builder.Id, builder.Version, default)).ReturnsAsync(true);
        _packages.Setup(p => p.FindOrNullAsync(builder.Id, builder.Version, true, default))
            .ReturnsAsync(new Package { Id = builder.Id, Version = builder.Version });

        // Act
        var result = await _target.IndexAsync(stream, default);

        // Assert
        Assert.Equal(PackageIndexingResult.PackageAlreadyExists, result);
    }

    [Fact]
    public async Task IndexAsync_WithValidPackage_ReturnsSuccess()
    {
        // Arrange
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.False;
        var builder = new PackageBuilder
        {
            Id = "bagetter-test",
            Version = NuGetVersion.Parse("1.0.0"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        var assemblyFile = GetType().Assembly.Location;
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyFile,
            TargetPath = "lib/Test.dll"
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(builder.Id, builder.Version, default)).ReturnsAsync(false);
        _packages.Setup(p => p.AddAsync(It.Is<Package>(p1 => p1.Id == builder.Id && p1.Version.ToString() == builder.Version.ToString()), default)).ReturnsAsync(PackageAddResult.Success);

        _storage.Setup(s => s.SavePackageContentAsync(It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), stream, It.IsAny<FileStream>(), default, default, default)).Returns(Task.CompletedTask);

        _search.Setup(s => s.IndexAsync(It.Is<Package>(p => p.Id == builder.Id && p.Version.ToString() == builder.Version.ToString()), default)).Returns(Task.CompletedTask);

        // Act
        var result = await _target.IndexAsync(stream, default);

        // Assert
        Assert.Equal(PackageIndexingResult.Success, result);
    }

    [Fact]
    public async Task RetentionRunsWhenOnlyPrereleaseLimitIsConfigured()
    {
        _retentionOptions.MaxPrereleaseVersions = 2;
        var builder = new PackageBuilder
        {
            Id = "bagetter-test",
            Version = NuGetVersion.Parse("1.0.0-beta.3"),
            Description = "Test Description",
        };
        builder.Authors.Add("Test Author");
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = GetType().Assembly.Location,
            TargetPath = "lib/Test.dll",
        });

        using var stream = new MemoryStream();
        builder.Save(stream);
        _packages.Setup(p => p.ExistsAsync(builder.Id, builder.Version, default))
            .ReturnsAsync(false);
        _packages.Setup(p => p.AddAsync(It.IsAny<Package>(), default))
            .ReturnsAsync(PackageAddResult.Success);
        _storage.Setup(s => s.SavePackageContentAsync(
            It.IsAny<Package>(), stream, It.IsAny<FileStream>(), null, null, default))
            .Returns(Task.CompletedTask);
        _search.Setup(s => s.IndexAsync(It.IsAny<Package>(), default))
            .Returns(Task.CompletedTask);
        _deleter.Setup(d => d.DeleteOldVersionsAsync(
            It.IsAny<Package>(), null, null, null, 2, default))
            .ReturnsAsync(0);

        var result = await _target.IndexAsync(stream, default);

        Assert.Equal(PackageIndexingResult.Success, result);
        _deleter.Verify(d => d.DeleteOldVersionsAsync(
            It.IsAny<Package>(), null, null, null, 2, default), Times.Once);
    }

    [Fact]
    public async Task WhenDatabaseAddFailsBecausePackageAlreadyExists_ReturnsPackageAlreadyExists()
    {
        using var stream = MakePackage();
        _packages.Setup(p => p.ExistsAsync("bagetter-test", NuGetVersion.Parse("1.0.0"), default))
            .ReturnsAsync(false);
        _packages.Setup(p => p.AddAsync(It.IsAny<Package>(), default))
            .ReturnsAsync(PackageAddResult.PackageAlreadyExists);
        _packages.Setup(p => p.FindOrNullAsync("bagetter-test", NuGetVersion.Parse("1.0.0"), true, default))
            .ReturnsAsync((Package)null);
        _storage.Setup(s => s.SavePackageContentAsync(It.IsAny<Package>(), stream, It.IsAny<FileStream>(), null, null, default))
            .Returns(Task.CompletedTask);
        _storage.Setup(s => s.DeleteAsync(It.Is<Package>(p => p.StorageRevision != null), default))
            .Returns(Task.CompletedTask);

        Assert.Equal(PackageIndexingResult.PackageAlreadyExists, await _target.IndexAsync(stream, default));
        _storage.Verify(s => s.DeleteAsync(It.Is<Package>(p => p.StorageRevision != null), default), Times.Once);
        _search.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ThrowsWhenStorageSaveThrows()
    {
        using var stream = MakePackage();
        _packages.Setup(p => p.ExistsAsync("bagetter-test", NuGetVersion.Parse("1.0.0"), default))
            .ReturnsAsync(false);
        _packages.Setup(p => p.FindOrNullAsync("bagetter-test", NuGetVersion.Parse("1.0.0"), true, default))
            .ReturnsAsync((Package)null);
        _storage.Setup(s => s.SavePackageContentAsync(It.IsAny<Package>(), stream, It.IsAny<FileStream>(), null, null, default))
            .ThrowsAsync(new IOException("Injected failure"));
        _storage.Setup(s => s.DeleteAsync(It.Is<Package>(p => p.StorageRevision != null), default))
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<IOException>(() => _target.IndexAsync(stream, default));
        _storage.Verify(s => s.DeleteAsync(It.Is<Package>(p => p.StorageRevision != null), default), Times.Once);
        _packages.Verify(p => p.AddAsync(It.IsAny<Package>(), default), Times.Never);
    }

    [Fact]
    public async Task FailedOverwriteMustNotDeleteOldRevision()
    {
        using var stream = MakePackage();
        _mockOptions.AllowPackageOverwrites = PackageOverwriteAllowed.True;
        var old = new Package { Id = "bagetter-test", Version = NuGetVersion.Parse("1.0.0"), StorageRevision = new string('a', 32) };
        _packages.Setup(p => p.ExistsAsync(old.Id, old.Version, default)).ReturnsAsync(true);
        _packages.Setup(p => p.FindOrNullAsync(old.Id, old.Version, true, default)).ReturnsAsync(old);
        _packages.Setup(p => p.ReplaceAsync(It.IsAny<Package>(), default)).ThrowsAsync(new IOException("DB unavailable"));
        _storage.Setup(s => s.SavePackageContentAsync(It.IsAny<Package>(), stream, It.IsAny<FileStream>(), null, null, default))
            .Returns(Task.CompletedTask);
        _storage.Setup(s => s.DeleteAsync(It.Is<Package>(p => p.StorageRevision != old.StorageRevision), default))
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<IOException>(() => _target.IndexAsync(stream, default));
        _storage.Verify(s => s.DeleteAsync(It.Is<Package>(p => p.StorageRevision != old.StorageRevision), default), Times.Once);
        _storage.Verify(s => s.DeleteAsync(old.Id, old.Version, default), Times.Never);
    }

    private MemoryStream MakePackage()
    {
        var builder = new PackageBuilder { Id = "bagetter-test", Version = NuGetVersion.Parse("1.0.0"), Description = "Test" };
        builder.Authors.Add("Test Author");
        builder.Files.Add(new PhysicalPackageFile { SourcePath = GetType().Assembly.Location, TargetPath = "lib/Test.dll" });
        var stream = new MemoryStream();
        builder.Save(stream);
        return stream;
    }
}
