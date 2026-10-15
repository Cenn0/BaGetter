using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace BaGetter.Tests;

/// <summary>
/// NuGet's PackageBaseAddress and RegistrationsBaseUrl resources require
/// both GET and HEAD. Keep method routing and response semantics aligned.
/// </summary>
public sealed class HeadProtocolIntegrationTests : IDisposable
{
    private readonly BaGetterApplication _app;
    private readonly HttpClient _client;

    public HeadProtocolIntegrationTests(ITestOutputHelper output)
    {
        _app = new BaGetterApplication(output);
        _client = _app.CreateClient();
    }

    [Theory]
    [InlineData("v3/package/testdata/index.json")]
    [InlineData("v3/package/testdata/1.2.3/testdata.1.2.3.nupkg")]
    [InlineData("v3/package/testdata/1.2.3/testdata.nuspec")]
    [InlineData("v3/registration/testdata/index.json")]
    [InlineData("v3/registration/testdata/1.2.3.json")]
    public async Task HeadMatchesGetWithoutTransmittingBody(string path)
    {
        using var package = TestResources.GetResourceStream(TestResources.Package);
        await _app.AddPackageAsync(package);

        using var get = await _client.GetAsync(path);
        using var request = new HttpRequestMessage(HttpMethod.Head, path);
        using var head = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(get.StatusCode, head.StatusCode);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("v3/package/absent/index.json")]
    [InlineData("v3/registration/absent/index.json")]
    public async Task HeadOfMissingPackageReturnsNotFound(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, path);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task PackageDownloadsDoNotAcceptPost()
    {
        using var response = await _client.PostAsync(
            "v3/package/testdata/1.2.3/testdata.1.2.3.nupkg",
            new StringContent(""));
        Assert.False(response.IsSuccessStatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
