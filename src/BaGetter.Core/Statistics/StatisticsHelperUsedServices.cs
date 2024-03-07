using System.Collections.Generic;

namespace BaGetter.Core.Statistics;

/// <summary>
/// Static service to help collecting the used services during the DI setup progress.
/// </summary>
public static class StatisticsHelperUsedServices
{
    private static readonly HashSet<string> KnownServices = [];

    /// <summary>
    /// Adds a service name to a list of known services.
    /// </summary>
    /// <param name="serviceName"></param>
    public static void AddServiceToServices(string serviceName)
    {
        KnownServices.Add(serviceName);
    }

    /// <summary>
    /// Return a list of known services which are in use.
    /// </summary>
    public static IEnumerable<string> GetUsedServices()
    {
        return KnownServices;
    }
}
