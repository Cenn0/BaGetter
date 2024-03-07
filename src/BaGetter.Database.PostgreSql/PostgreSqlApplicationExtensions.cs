using System;
using BaGetter.Core;
using BaGetter.Core.Statistics;
using BaGetter.Database.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BaGetter;

public static class PostgreSqlApplicationExtensions
{
    private const string PostgreSql = "PostgreSql";

    public static BaGetterApplication AddPostgreSqlDatabase(this BaGetterApplication app, IConfiguration configuration)
    {
        if (!configuration.HasDatabaseType(PostgreSql)) return app;

        app.Services.AddBaGetDbContextProvider<PostgreSqlContext>(PostgreSql);
        StatisticsHelperUsedServices.AddServiceToServices(PostgreSql);

        return app;
    }

    public static BaGetterApplication AddPostgreSqlDatabase(
        this BaGetterApplication app,
        IConfiguration configuration,
        Action<DatabaseOptions> configure)
    {
        app.AddPostgreSqlDatabase(configuration);
        app.Services.Configure(configure);
        return app;
    }
}
