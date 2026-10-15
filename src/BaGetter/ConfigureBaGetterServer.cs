using System;
using System.Net;
using BaGetter.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace BaGetter;

public class ConfigureBaGetterServer
    : IConfigureOptions<CorsOptions>
    , IConfigureOptions<FormOptions>
    , IConfigureOptions<ForwardedHeadersOptions>
    , IConfigureOptions<IISServerOptions>
{
    public const string CorsPolicy = "ConfiguredOrigins";
    private readonly BaGetterOptions _baGetterOptions;
    private readonly IConfiguration _configuration;

    public ConfigureBaGetterServer(IOptions<BaGetterOptions> baGetterOptions, IConfiguration configuration)
    {
        _baGetterOptions = baGetterOptions.Value;
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }


    public void Configure(CorsOptions options)
    {
        // NuGet CLI clients do not require browser CORS. Enable it explicitly.
        var origins = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        options.AddPolicy(CorsPolicy, builder =>
        {
            if (origins.Length > 0)
            {
                builder.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
            }
        });
    }

    public void Configure(FormOptions options)
    {
        options.MultipartBodyLengthLimit = GetMaxRequestBodyBytes(
            _baGetterOptions.MaxPackageSizeGiB, _baGetterOptions.MaxRequestBodySizeMiB);
    }

    public static long GetMaxRequestBodyBytes(uint maxPackageSizeGiB, uint? maxRequestBodySizeMiB)
    {
        if (maxPackageSizeGiB == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPackageSizeGiB), "The package size must be positive");
        }
        if (maxRequestBodySizeMiB == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequestBodySizeMiB), "The request limit must be positive");
        }

        const long mib = 1024L * 1024;
        var packageLimit = checked((long)maxPackageSizeGiB * 1024 * mib);
        // The optional MiB setting can reduce, but never bypass, the GiB cap.
        return maxRequestBodySizeMiB.HasValue
            ? Math.Min(packageLimit, checked((long)maxRequestBodySizeMiB.Value * mib))
            : packageLimit;
    }

    public void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

        // Retain the framework's trusted-loopback defaults. Only configured
        // reverse-proxy addresses are allowed to supply forwarded headers.
        var proxies = _configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException($"Invalid ForwardedHeaders:KnownProxies address: '{proxy}'");
            }

            options.KnownProxies.Add(address);
        }
    }

    public void Configure(IISServerOptions options)
    {
        options.MaxRequestBodySize = GetMaxRequestBodyBytes(
            _baGetterOptions.MaxPackageSizeGiB, _baGetterOptions.MaxRequestBodySizeMiB);
    }
}
