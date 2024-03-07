using System;
using BaGetter.Core;
using BaGetter.Core.Statistics;
using BaGetter.Gcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BaGetter;

public static class GoogleCloudApplicationExtensions
{
    private const string GoogleCloud = "GoogleCloud";

    public static BaGetterApplication AddGoogleCloudStorage(this BaGetterApplication app, IConfiguration configuration)
    {
        if (!configuration.HasStorageType(GoogleCloud)) return app;

        StatisticsHelperUsedServices.AddServiceToServices(GoogleCloud);

        app.Services.AddBaGetterOptions<GoogleCloudStorageOptions>(nameof(BaGetterOptions.Storage));
        app.Services.AddTransient<GoogleCloudStorageService>();

        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<GoogleCloudStorageService>());

        app.Services.AddProvider<IStorageService>((provider, config) =>
        {
            if (!config.HasStorageType("GoogleCloud")) return null;

            return provider.GetRequiredService<GoogleCloudStorageService>();
        });

        return app;
    }

    public static BaGetterApplication AddGoogleCloudStorage(
        this BaGetterApplication app,
        IConfiguration configuration,
        Action<GoogleCloudStorageOptions> configure)
    {
        app.AddGoogleCloudStorage(configuration);
        app.Services.Configure(configure);
        return app;
    }
}
