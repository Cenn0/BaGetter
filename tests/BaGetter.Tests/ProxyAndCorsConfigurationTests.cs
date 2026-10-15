using System.Collections.Generic;
using System.Net;
using BaGetter.Core;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGetter.Tests;

public class ProxyAndCorsConfigurationTests
{
    [Fact]
    public void DefaultConfigurationDoesNotTrustArbitraryForwardedHeaders()
    {
        var configurator = Create(new Dictionary<string, string>());
        var options = new ForwardedHeadersOptions();
        configurator.Configure(options);

        Assert.NotEmpty(options.KnownProxies);
        Assert.DoesNotContain(IPAddress.Parse("203.0.113.25"), options.KnownProxies);
    }

    [Fact]
    public void ExplicitProxyAddressCanBeTrusted()
    {
        var configurator = Create(new Dictionary<string, string>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "192.0.2.15"
        });
        var options = new ForwardedHeadersOptions();
        configurator.Configure(options);

        Assert.Contains(IPAddress.Parse("192.0.2.15"), options.KnownProxies);
    }

    [Fact]
    public void CrossOriginAccessIsNotAllowedByDefault()
    {
        var configurator = Create(new Dictionary<string, string>());
        var options = new CorsOptions();
        configurator.Configure(options);

        Assert.Empty(options.GetPolicy(ConfigureBaGetterServer.CorsPolicy).Origins);
    }

    [Fact]
    public void CrossOriginAccessCanBeAllowlisted()
    {
        var configurator = Create(new Dictionary<string, string>
        {
            ["Cors:AllowedOrigins:0"] = "https://packages.example.org"
        });
        var options = new CorsOptions();
        configurator.Configure(options);

        Assert.Equal(new[] { "https://packages.example.org" },
            options.GetPolicy(ConfigureBaGetterServer.CorsPolicy).Origins);
    }

    private static ConfigureBaGetterServer Create(Dictionary<string, string> data)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        return new ConfigureBaGetterServer(Options.Create(new BaGetterOptions()), configuration);
    }
}
