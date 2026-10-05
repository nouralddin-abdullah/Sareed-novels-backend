using Application.Chapters.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Publishes the scheduled chapters whose time has come (#77, <see cref="ScheduledChapterPublisher"/>): as soon as the app
/// starts, then every minute, each run in a scope of its own. While the host has stopped the app (runasp.net stops an
/// idle app) nothing runs: due chapters come out when it starts again, at this service's first run, or before a request
/// reading their novel is answered (<see cref="PublishDueChaptersBehavior{TRequest,TResponse}"/>), whichever is first.
/// </summary>
public sealed class ScheduledChapterPublishingService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ScheduledChapterPublishingService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // don't hold up app startup

        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One run: publishes every due chapter and answers how many. A failure is logged; the next run tries again.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ScheduledChapterPublisher>().PublishDueAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Publishing scheduled chapters failed; retrying in {Interval}", Interval);
            return 0;
        }
    }
}
