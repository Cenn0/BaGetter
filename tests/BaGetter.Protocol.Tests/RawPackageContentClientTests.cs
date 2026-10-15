using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using System.Threading.Tasks;
using BaGetter.Protocol.Internal;
using Xunit;

namespace BaGetter.Protocol.Tests;

public class RawPackageContentTests : IClassFixture<ProtocolFixture>
{
    private readonly RawPackageContentClient _target;

    public RawPackageContentTests(ProtocolFixture fixture)
    {
        _target = fixture.ContentClient;
    }

    [Fact]
    public async Task GetsPackageVersions()
    {
        var result = await _target.GetPackageVersionsOrNullAsync("Test.Package");

        Assert.NotNull(result);
        Assert.Equal(2, result.Versions.Count);
        Assert.Equal("1.0.0", result.Versions[0]);
        Assert.Equal("2.0.0", result.Versions[1]);
    }

    [Fact]
    public async Task ReturnsNullIfPackageDoesNotExist()
    {
        var result = await _target.GetPackageVersionsOrNullAsync(Guid.NewGuid().ToString());

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task PackageAndManifestDoNotAcceptUpstreamFailuresAsContent(HttpStatusCode status)
    {
        using var http = new HttpClient(new StubHandler(status));
        var client = new RawPackageContentClient(http, "https://example.test/v3/package");
        var version = NuGetVersion.Parse("1.2.3");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.DownloadPackageOrNullAsync("Test.Package", version));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.DownloadPackageManifestOrNullAsync("Test.Package", version));
    }

    [Fact]
    public async Task MissingPackageAndManifestReturnNull()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.NotFound));
        var client = new RawPackageContentClient(http, "https://example.test/v3/package");
        var version = NuGetVersion.Parse("1.2.3");

        Assert.Null(await client.DownloadPackageOrNullAsync("Test.Package", version));
        Assert.Null(await client.DownloadPackageManifestOrNullAsync("Test.Package", version));
    }

    [Fact]
    public async Task SuccessfulPackageDownloadReturnsOriginalBytes()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK));
        var client = new RawPackageContentClient(http, "https://example.test/v3/package");
        using var stream = await client.DownloadPackageOrNullAsync("Test.Package", NuGetVersion.Parse("1.2.3"));
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        Assert.Equal(new byte[] { 1, 2, 3 }, copy.ToArray());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public StubHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
            });
    }
}
