using System;
using BaGetter.Core;
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
    public const string CorsPolicy = "AllowAll";
    private readonly BaGetterOptions _baGetterOptions;

    public ConfigureBaGetterServer(IOptions<BaGetterOptions> baGetterOptions)
    {
        _baGetterOptions = baGetterOptions.Value;
    }


    public void Configure(CorsOptions options)
    {
        // TODO: Consider disabling this on production builds.
        options.AddPolicy(
            CorsPolicy,
            builder => builder.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());
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

        // Do not restrict to local network/proxy
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }

    public void Configure(IISServerOptions options)
    {
        options.MaxRequestBodySize = GetMaxRequestBodyBytes(
            _baGetterOptions.MaxPackageSizeGiB, _baGetterOptions.MaxRequestBodySizeMiB);
    }
}
