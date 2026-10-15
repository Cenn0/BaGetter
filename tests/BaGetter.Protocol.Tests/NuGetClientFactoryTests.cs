using Xunit;

namespace BaGetter.Protocol.Tests;

public class NuGetClientFactoryTests : IClassFixture<ProtocolFixture>
{
    private readonly NuGetClientFactory _target;

    public NuGetClientFactoryTests(ProtocolFixture fixture)
    {
        _target = fixture.NuGetClientFactory;
    }

    [Fact]
    public void FactoryBasedClientRetainsActualConfiguredUpstreamUrl()
    {
        Assert.Equal(TestData.ServiceIndexUrl, _target.ServiceIndexUrl);
        Assert.Equal(TestData.ServiceIndexUrl, new NuGetClient(_target).ServiceIndexUrl);
    }

    // TODO: Test package download
    // TODO: Test package manifest download
    // TODO: List package versions
    // TODO: List package versions #2
    // TODO: List all package versions
    // TODO: Get package metadata inlined
    // TODO: Get package metadata paged
    // TODO: Get single package metadata inlined
    // TODO: Get single package metadata paged
    // TODO: Search
    // TODO: Autocomplete
}
