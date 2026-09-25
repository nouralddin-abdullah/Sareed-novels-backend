using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Rebuilds the global supporter leaderboards (weekly = last 7 days, all-time) shortly after startup and then hourly.
/// Nothing scheduled them before: they only changed when an admin called the recalculate endpoints, so the weekly
/// board never rolled over and new gifts never appeared on /leaderboard.
/// </summary>
public class GiftLeaderboardRecalculationService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<GiftLeaderboardRecalculationService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, timeProvider, stoppingToken);

        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var leaderboards = scope.ServiceProvider.GetRequiredService<IGlobalSupporterLeaderboardRepository>();
                await leaderboards.RecalculateWeeklyLeaderboard();
                await leaderboards.RecalculateAllTimeLeaderboard();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Gift leaderboard recalculation failed; retrying in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
