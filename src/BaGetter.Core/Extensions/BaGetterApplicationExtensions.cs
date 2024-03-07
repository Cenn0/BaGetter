using System;
using BaGetter.Core;
using BaGetter.Core.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BaGetter;

public static class BaGetterApplicationExtensions
{
    private const string FileSystem = "FileSystem";

    public static BaGetterApplication AddFileStorage(this BaGetterApplication app, IConfiguration configuration)
    {
        if (!configuration.HasStorageType(FileSystem)) return app;

        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<FileStorageService>());
        StatisticsHelperUsedServices.AddServiceToServices(FileSystem);

        return app;
    }

    public static BaGetterApplication AddFileStorage(
        this BaGetterApplication app,
        IConfiguration configuration,
        Action<FileSystemStorageOptions> configure)
    {
        app.AddFileStorage(configuration);
        app.Services.Configure(configure);
        return app;
    }

    public static BaGetterApplication AddNullStorage(this BaGetterApplication app)
    {
        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<NullStorageService>());
        return app;
    }

    public static BaGetterApplication AddNullSearch(this BaGetterApplication app)
    {
        app.Services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
        app.Services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<NullSearchService>());
        return app;
    }

    public static BaGetterApplication AddStatistics(this BaGetterApplication app)
    {
        app.Services.TryAddSingleton<IStatisticsService, StatisticsService>();
        return app;
    }
}
