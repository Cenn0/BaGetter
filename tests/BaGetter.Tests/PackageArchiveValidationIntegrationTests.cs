using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace BaGetter.Tests;

public sealed class PackageArchiveValidationIntegrationTests : IDisposable
{
    private readonly BaGetterApplication _app;

    public PackageArchiveValidationIntegrationTests(ITestOutputHelper output)
    {
        _app = new BaGetterApplication(output);
    }

    [Fact]
    public async Task RejectsPackageWithUnsafeArchiveEntryBeforeStoringIt()
    {
        using var package = TestResources.GetResourceStream(TestResources.Package);
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.CreateEntry("lib/../../outside.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("should never be served");
        }
        package.Position = 0;

        using var scope = _app.Services.CreateScope();
        var indexing = scope.ServiceProvider.GetRequiredService<IPackageIndexingService>();
        var result = await indexing.IndexAsync(package, CancellationToken.None);

        Assert.Equal(PackageIndexingResult.InvalidPackage, result);
        var packages = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
        Assert.False(await packages.ExistsAsync("TestData", CancellationToken.None));
    }

    [Fact]
    public async Task CancellationDuringValidationIsPropagated()
    {
        using var package = TestResources.GetResourceStream(TestResources.Package);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var scope = _app.Services.CreateScope();
        var indexing = scope.ServiceProvider.GetRequiredService<IPackageIndexingService>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => indexing.IndexAsync(package, cancellation.Token));
    }

    public void Dispose() => _app.Dispose();
}
