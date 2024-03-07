using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BaGetter.Core.Statistics;

public class StatisticsService : IStatisticsService
{
    private readonly IServiceProvider _serviceProvider;

    public StatisticsService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<int> GetPackagesTotalAmount()
    {
        var dbContext = GetDbContext();
        var packagesCount = await dbContext.Packages.GroupBy(p => p.Id).CountAsync();
        return packagesCount;
    }

    public async Task<int> GetVersionsTotalAmount()
    {
        var dbContext = GetDbContext();
        var packagesVersionsCount = await dbContext.Packages.CountAsync();
        return packagesVersionsCount;
    }

    public void ServiceMakeKnownToStatsService(string serviceName)
    {
        StatisticsHelperUsedServices.AddServiceToServices(serviceName);
    }

    public IEnumerable<string> GetKnownServices()
    {
        return StatisticsHelperUsedServices.GetUsedServices();
    }

    private IContext GetDbContext()
    {
        var newScope = _serviceProvider.CreateScope();
        var dbContext = newScope.ServiceProvider.GetRequiredService<IContext>();

        return dbContext;
    }
}
