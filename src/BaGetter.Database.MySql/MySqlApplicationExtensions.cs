using System;
using BaGetter.Core;
using BaGetter.Core.Statistics;
using BaGetter.Database.MySql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BaGetter;

public static class MySqlApplicationExtensions
{
    private const string MySql = "MySql";

    public static BaGetterApplication AddMySqlDatabase(this BaGetterApplication app, IConfiguration configuration)
    {
        if (!configuration.HasDatabaseType(MySql)) return app;

        app.Services.AddBaGetDbContextProvider<MySqlContext>(MySql);
        StatisticsHelperUsedServices.AddServiceToServices(MySql);

        return app;
    }

    public static BaGetterApplication AddMySqlDatabase(
        this BaGetterApplication app,
        IConfiguration configuration,
        Action<DatabaseOptions> configure)
    {
        app.AddMySqlDatabase(configuration);
        app.Services.Configure(configure);
        return app;
    }
}
