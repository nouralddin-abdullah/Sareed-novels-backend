using Infrastructure.Push;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Sends queued push notifications (<see cref="PushOutboxProcessor"/>): right away when a notification wakes it
/// (<see cref="PushOutboxSignal"/>), otherwise every <see cref="PushDeliveryOptions.PollInterval"/> for retries and rows
/// queued before a recycle. Hourly, it deletes finished rows. Without FCM credentials it says so once at startup
/// and marks queued pushes as skipped instead of sending.
/// </summary>
public sealed class PushNotificationWorker(
    IServiceScopeFactory scopeFactory,
    FcmConnection fcm,
    PushOutboxSignal signal,
    IOptions<PushDeliveryOptions> options,
    TimeProvider timeProvider,
    ILogger<PushNotificationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan MaxErrorDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // don't hold up app startup

        if (fcm.IsEnabled)
        {
            logger.LogInformation("Push notifications are on (Firebase project {ProjectId})", fcm.ProjectId);
        }
        else
        {
            logger.LogWarning("Push notifications are disabled: {Reason}. Devices can still register, but nothing is sent "
                              + "until Fcm__ServiceAccountJson is configured (see README.md).", fcm.DisabledReason);
        }

        var nextCleanup = timeProvider.GetUtcNow();
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var moreWaiting = false;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<PushOutboxProcessor>();
                if (fcm.IsEnabled)
                {
                    moreWaiting = await processor.ProcessDueAsync(stoppingToken) >= options.Value.BatchSize;
                }
                else
                {
                    await processor.SkipPendingAsync($"push is disabled: {fcm.DisabledReason}", stoppingToken);
                }

                if (timeProvider.GetUtcNow() >= nextCleanup)
                {
                    await processor.DeleteFinishedAsync(stoppingToken);
                    nextCleanup = timeProvider.GetUtcNow() + options.Value.CleanupInterval;
                }
                failures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Claimed rows come back after their lease, so nothing is lost; back off while the database is down.
                failures++;
                var delay = TimeSpan.FromSeconds(Math.Min(15 * Math.Pow(2, failures - 1), MaxErrorDelay.TotalSeconds));
                logger.LogError(ex, "Sending push notifications failed; trying again in {Delay}", delay);
                try
                {
                    await Task.Delay(delay, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                continue;
            }

            if (!moreWaiting)
            {
                try
                {
                    await signal.WaitAsync(options.Value.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
