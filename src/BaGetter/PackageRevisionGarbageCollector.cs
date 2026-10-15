using System;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGetter;

/// <summary>
/// Cleans up crash leftovers and superseded immutable filesystem revisions.
/// The database is always queried before deleting any candidate.
/// </summary>
public sealed class PackageRevisionGarbageCollector : BackgroundService
{
    private static readonly TimeSpan MinimumAge = TimeSpan.FromDays(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PackageRevisionGarbageCollector> _logger;

    public PackageRevisionGarbageCollector(
        IServiceScopeFactory scopes, ILogger<PackageRevisionGarbageCollector> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
                if (storage is FileStorageService filesystem)
                {
                    var database = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
                    var removed = await filesystem.CleanupUnreferencedRevisionsAsync(
                        async (id, version, revision, ct) =>
                        {
                            var package = await database.FindOrNullAsync(
                                id, version, includeUnlisted: true, ct);
                            return string.Equals(package?.StorageRevision, revision, StringComparison.Ordinal);
                        },
                        MinimumAge, stoppingToken);

                    if (removed > 0)
                        _logger.LogInformation("Removed {Count} unreferenced package content revisions", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                // Fail closed: never delete content when DB consistency cannot be confirmed.
                _logger.LogError(error, "Package content revision cleanup failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
