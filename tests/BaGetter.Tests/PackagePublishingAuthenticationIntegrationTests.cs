using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace BaGetter.Tests;

public class PackagePublishingAuthenticationIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public PackagePublishingAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("incorrect")]
    public async Task UploadRejectsMissingOrInvalidKey(string suppliedKey)
    {
        using var app = new BaGetterApplication(_output);
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v2/package");
        request.Content = new StreamContent(TestResources.GetResourceStream(TestResources.Package));
        if (suppliedKey != null)
        {
            request.Headers.Add("X-NuGet-ApiKey", suppliedKey);
        }

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("DELETE", "/api/v2/package/TestData/1.2.3")]
    [InlineData("POST", "/api/v2/package/TestData/1.2.3")]
    [InlineData("PUT", "/api/v2/symbol")]
    public async Task PackageAndSymbolMutationsRejectMissingKey(string method, string path)
    {
        using var app = new BaGetterApplication(_output);
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadSucceedsWithConfiguredKey()
    {
        const string key = "integration-test-key";
        using var app = new BaGetterApplication(_output, inMemoryConfiguration: config => config["ApiKey"] = key);
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v2/package");
        request.Headers.Add("X-NuGet-ApiKey", key);
        request.Content = new StreamContent(TestResources.GetResourceStream(TestResources.Package));

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousReadersCanStillDiscoverFeed()
    {
        using var app = new BaGetterApplication(_output);
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/v3/index.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
