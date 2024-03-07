using System;
using Aliyun.OSS;
using BaGetter.Aliyun;
using BaGetter.Core;
using BaGetter.Core.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BaGetter;

public static class AliyunApplicationExtensions
{
    private const string AliyunOss = "AliyunOss";

    public static BaGetterApplication AddAliyunOssStorage(this BaGetterApplication app, IConfiguration configuration)
    {
        if (!configuration.HasStorageType(AliyunOss)) return app;

        StatisticsHelperUsedServices.AddServiceToServices(AliyunOss);

        app.Services.AddBaGetterOptions<AliyunStorageOptions>(nameof(BaGetterOptions.Storage));

        app.Services.AddTransient<AliyunStorageService>();
        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<AliyunStorageService>());

        app.Services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AliyunStorageOptions>>().Value;

            return new OssClient(options.Endpoint, options.AccessKey, options.AccessKeySecret);
        });

        app.Services.AddProvider<IStorageService>((provider, config) =>
        {
            if (!config.HasStorageType("AliyunOss")) return null;

            return provider.GetRequiredService<AliyunStorageService>();
        });

        return app;
    }

    public static BaGetterApplication AddAliyunOssStorage(
        this BaGetterApplication app,
        IConfiguration configuration,
        Action<AliyunStorageOptions> configure)
    {
        app.AddAliyunOssStorage(configuration);
        app.Services.Configure(configure);
        return app;
    }
}
