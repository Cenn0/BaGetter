using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGetter.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Packaging;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace BaGetter.Tests;

/// <summary>Real SQLite and filesystem tests of package revision commit/rollback.</summary>
public class AtomicPackagePublicationIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public AtomicPackagePublicationIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task OverwriteSwitchesContentAndMetadataWithoutDeletingOldRevision()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();
        using var first = MakePackage("original description");
        await server.AddPackageAsync(first);
        var old = await FindPackageAsync(server);
        Assert.NotNull(old.StorageRevision);

        using var second = MakePackage("replacement description");
        await server.AddPackageAsync(second);
        var current = await FindPackageAsync(server);
        Assert.NotEqual(old.StorageRevision, current.StorageRevision);
        using var newResponse = await client.GetAsync("v3/package/atomic.test/1.2.3/atomic.test.nuspec");
        Assert.Equal(HttpStatusCode.OK, newResponse.StatusCode);
        Assert.Contains("replacement description", await newResponse.Content.ReadAsStringAsync());

        using var scope = server.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IPackageStorageService>();
        using var oldNuspec = await storage.GetNuspecStreamAsync(old, default);
        using var reader = new StreamReader(oldNuspec);
        Assert.Contains("original description", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task SqliteCommitFailureLeavesPriorPackageDownloadable()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();
        using var first = MakePackage("preserve me");
        await server.AddPackageAsync(first);
        var old = await FindPackageAsync(server);
        using (var scope = server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER FailPackageReplacement BEFORE INSERT ON Packages BEGIN SELECT RAISE(ABORT, 'Injected failure'); END;");
        }

        using var second = MakePackage("failed replacement");
        await Assert.ThrowsAnyAsync<Exception>(() => server.AddPackageAsync(second));
        var retained = await FindPackageAsync(server);
        Assert.Equal(old.StorageRevision, retained.StorageRevision);
        using var response = await client.GetAsync("v3/package/atomic.test/1.2.3/atomic.test.nuspec");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("preserve me", await response.Content.ReadAsStringAsync());
    }

    private BaGetterApplication CreateServer() => new(_output, inMemoryConfiguration: config =>
        config["AllowPackageOverwrites"] = "True");

    private static async Task<Package> FindPackageAsync(BaGetterApplication server)
    {
        using var scope = server.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
        var result = await database.FindOrNullAsync("atomic.test", NuGetVersion.Parse("1.2.3"), true, default);
        Assert.NotNull(result);
        return result;
    }

    private static MemoryStream MakePackage(string description)
    {
        var builder = new PackageBuilder
        {
            Id = "atomic.test",
            Version = NuGetVersion.Parse("1.2.3"),
            Description = description,
        };
        builder.Authors.Add("Test Author");
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = typeof(AtomicPackagePublicationIntegrationTests).Assembly.Location,
            TargetPath = "lib/net10.0/Atomic.dll",
        });
        var stream = new MemoryStream();
        builder.Save(stream);
        return stream;
    }
}
