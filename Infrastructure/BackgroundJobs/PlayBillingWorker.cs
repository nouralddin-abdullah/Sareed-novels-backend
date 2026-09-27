using Infrastructure.PlayBilling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Google Play Billing upkeep: every 5 minutes, consumes purchases whose consume failed or never ran (Google refunds a
/// purchase nobody consumed or acknowledged within three days); every hour, reads Google's voided purchases and takes
/// their points back. Both resume where they left off after a restart: owed consumes are rows in PlayPurchases, and the
/// voided-purchases cursor is stored in PlaySyncCursors. Does nothing, and says so at startup, while billing is disabled.
/// </summary>
public class PlayBillingWorker(
    IServiceScopeFactory scopeFactory,
    PlayBillingConnection connection,
    TimeProvider timeProvider,
    ILogger<PlayBillingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan VoidedSyncInterval = TimeSpan.FromHours(1);

    private DateTime? lastVoidedSync;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!connection.IsEnabled)
        {
            logger.LogWarning("Google Play billing is disabled: {Reason}. The app can't sell point packs until it is configured (README.md)",
                connection.DisabledReason);
            return;
        }

        logger.LogInformation("Google Play billing is on for {PackageName}: {Products}{TestPurchases}", connection.PackageName,
            string.Join(", ", connection.Products.Select(p => $"{p.ProductId}={p.Points}")),
            connection.AllowTestPurchases ? "; license-test purchases are credited" : "");
        if (connection.IgnoredProducts.Count > 0)
        {
            logger.LogWarning("PlayBilling:Products entries ignored (not a valid Play product id, or the points aren't a number): {Ignored}",
                string.Join(", ", connection.IgnoredProducts));
        }

        await Task.Delay(StartupDelay, timeProvider, stoppingToken);

        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One tick: owed consumes always, the voided-purchases poll when an hour has passed since the last one.</summary>
    internal async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PlayBillingService>().RetryConsumptionsAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Retrying Google Play consumes failed; retrying in {Interval}", Interval);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (lastVoidedSync is { } last && now - last < VoidedSyncInterval)
        {
            return;
        }

        // An hour until the next poll even after a failure: the cursor didn't move, so nothing is lost meanwhile.
        lastVoidedSync = now;
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PlayBillingService>().SyncVoidedPurchasesAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reading voided Google Play purchases failed; retrying in {Interval}", VoidedSyncInterval);
        }
    }
}
