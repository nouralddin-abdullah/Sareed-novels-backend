using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Recalculates rankings shortly after startup and then every 30 minutes, and once a day prunes per-visitor view
/// rows past their retention. Runs inside the API, replacing the Azure Functions timer that silently stopped.
/// </summary>
public class RankingRecalculationService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RankingRecalculationService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan UniqueViewRetention = TimeSpan.FromDays(120);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, timeProvider, stoppingToken);

        DateOnly? lastPruned = null;
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IRankingService>().CalculateAllGenreRankings();

                var now = timeProvider.GetUtcNow().UtcDateTime;
                var today = DateOnly.FromDateTime(now);
                if (lastPruned != today)
                {
                    var pruned = await scope.ServiceProvider.GetRequiredService<IViewTrackingService>()
                        .PruneUniqueViewsAsync(now - UniqueViewRetention, stoppingToken);
                    lastPruned = today;
                    logger.LogInformation("Pruned {Count} per-visitor view rows older than {Days} days",
                        pruned, UniqueViewRetention.TotalDays);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ranking recalculation failed; retrying in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
