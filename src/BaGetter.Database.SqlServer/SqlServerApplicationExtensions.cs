using System;
using BaGetter.Core;
using BaGetter.Core.Statistics;
using BaGetter.Database.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BaGetter;

public static class SqlServerApplicationExtensions
{
    private const string SqlServer = "SqlServer";

    public static BaGetterApplication AddSqlServerDatabase(this BaGetterApplication app, IConfiguration configuration)
    {
        if (!configuration.HasDatabaseType(SqlServer)) return app;

        app.Services.AddBaGetDbContextProvider<SqlServerContext>(SqlServer);
        StatisticsHelperUsedServices.AddServiceToServices(SqlServer);

        return app;
    }

    public static BaGetterApplication AddSqlServerDatabase(
        this BaGetterApplication app,
        IConfiguration configuration,
        Action<DatabaseOptions> configure)
    {
        app.AddSqlServerDatabase(configuration);
        app.Services.Configure(configure);
        return app;
    }
}
