using System;
using BaGetter.Core;
using BaGetter.Core.Statistics;
using BaGetter.Database.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BaGetter;

public static class SqliteApplicationExtensions
{
    private const string Sqlite = "Sqlite";

    public static BaGetterApplication AddSqliteDatabase(this BaGetterApplication app, IConfiguration configuration)
    {
        if (!configuration.HasDatabaseType(Sqlite)) return app;

        app.Services.AddBaGetDbContextProvider<SqliteContext>(Sqlite);
        StatisticsHelperUsedServices.AddServiceToServices(Sqlite);

        return app;
    }

    public static BaGetterApplication AddSqliteDatabase(
        this BaGetterApplication app,
        IConfiguration configuration,
        Action<DatabaseOptions> configure)
    {
        app.AddSqliteDatabase(configuration);
        app.Services.Configure(configure);
        return app;
    }
}
