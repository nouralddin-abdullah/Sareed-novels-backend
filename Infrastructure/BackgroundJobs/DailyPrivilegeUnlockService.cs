using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Unlocks one privilege chapter per novel per UTC day. <see cref="IPrivilegeService.PerformDailyUnlockAsync"/>
/// skips novels already unlocked today, so checking hourly is safe and picks up midnight within the hour, even after
/// restarts. Days missed while no job was running are intentionally not back-filled.
/// </summary>
public class DailyPrivilegeUnlockService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<DailyPrivilegeUnlockService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
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
                await scope.ServiceProvider.GetRequiredService<IPrivilegeService>().PerformDailyUnlockAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Daily privilege unlock failed; retrying in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
