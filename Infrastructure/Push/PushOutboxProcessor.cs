using Domain.Constants;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Push;

/// <summary>Tuning for push delivery. The defaults suit the small shared host (256-512 MB).</summary>
public sealed class PushDeliveryOptions
{
    /// <summary>Outbox rows claimed per batch: bounds memory and how long one batch takes.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>FCM requests in flight at once (FCM takes one message per request).</summary>
    public int MaxParallelSends { get; set; } = 8;

    /// <summary>Attempts before a push that keeps failing (5xx, 429, network, credentials) is given up and logged.</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>First retry delay, doubled per attempt (plus up to 20% jitter) up to <see cref="MaxRetryDelay"/>; a longer Retry-After from FCM wins.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a claimed row belongs to its worker: if the process dies mid-batch, the row is retried after this.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Pushes still waiting after this (the app was down for hours) are skipped as stale.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Finished rows (sent, failed, skipped) are deleted after this.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(3);

    /// <summary>How often the worker looks for due rows when no new push wakes it.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Drains the push outbox one bounded batch at a time: claims due rows, skips what shouldn't go out (group switched
/// off, device gone, stale), sends the rest with bounded parallelism, then records each result: sent; dead token
/// (device removed); retry later with exponential backoff; or failed for good, logged as a warning. The in-app
/// notification is never touched.
/// </summary>
public sealed class PushOutboxProcessor(
    ApplicationDbContext dbContext,
    IPushService pushService,
    PushTargetResolver targetResolver,
    IOptions<PushDeliveryOptions> options,
    TimeProvider timeProvider,
    ILogger<PushOutboxProcessor> logger)
{
    private const int DeleteBatchSize = 1000;

    private PushDeliveryOptions Options => options.Value;
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Sends one batch of due pushes. Returns how many rows it handled; 0 means nothing was due.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        var rows = await ClaimDueAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return 0;
        }

        var report = new BatchReport();
        var sends = await PrepareAsync(rows, report, cancellationToken);
        if (sends.Count > 0)
        {
            var results = await SendAllAsync(sends, cancellationToken);
            await RecordAsync(sends, results, report, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        report.Log(logger);
        return rows.Count;
    }

    /// <summary>With push disabled (no FCM credentials), marks everything waiting as skipped, with the reason.</summary>
    public async Task<int> SkipPendingAsync(string reason, CancellationToken cancellationToken)
    {
        var now = Now;
        var error = Shorten(reason);
        return await dbContext.PushOutbox
            .Where(o => o.Status == PushOutboxStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, PushOutboxStatus.Skipped)
                .SetProperty(o => o.CompletedAt, (DateTime?)now)
                .SetProperty(o => o.LastError, error), cancellationToken);
    }

    /// <summary>Deletes finished rows older than <see cref="PushDeliveryOptions.Retention"/>, in small batches.</summary>
    public async Task<int> DeleteFinishedAsync(CancellationToken cancellationToken)
    {
        var cutoff = Now - Options.Retention;
        var total = 0;
        int deleted;
        do
        {
            deleted = await dbContext.PushOutbox
                .Where(o => o.Status != PushOutboxStatus.Pending && o.CreatedAt < cutoff)
                .OrderBy(o => o.CreatedAt)
                .Take(DeleteBatchSize)
                .ExecuteDeleteAsync(cancellationToken);
            total += deleted;
        }
        while (deleted == DeleteBatchSize);

        if (total > 0)
        {
            logger.LogInformation("Deleted {Count} finished push notifications older than {Retention}", total, Options.Retention);
        }
        return total;
    }

    private Task<List<PushOutboxMessage>> ClaimDueAsync(CancellationToken cancellationToken)
    {
        var now = Now;
        var leaseEnd = now + Options.Lease;
        // Oldest due rows first. READPAST skips rows another worker (an overlapping app-pool instance) is claiming, and
        // moving NextAttemptAt to the end of the lease keeps them from being claimed again while this one works.
        // Status = 0 (Pending) is a literal so the filtered index IX_PushOutbox_Due applies.
        return dbContext.PushOutbox
            .FromSql($"""
                WITH due AS (
                    SELECT TOP ({Options.BatchSize}) *
                    FROM PushOutbox WITH (ROWLOCK, READPAST, UPDLOCK)
                    WHERE Status = 0 AND NextAttemptAt <= {now}
                    ORDER BY NextAttemptAt)
                UPDATE due SET Attempts = Attempts + 1, NextAttemptAt = {leaseEnd}
                OUTPUT inserted.*
                """)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Send>> PrepareAsync(List<PushOutboxMessage> rows, BatchReport report, CancellationToken cancellationToken)
    {
        var notificationIds = rows.Select(r => r.NotificationId).Distinct().ToList();
        var notifications = await dbContext.Notifications
            .AsNoTracking()
            .Where(n => notificationIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, cancellationToken);

        var deviceIds = rows.Select(r => r.DeviceId).Distinct().ToList();
        var devices = await dbContext.UserDevices
            .AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var recipientIds = notifications.Values.Select(n => n.UserId).Distinct().ToList();
        var preferences = await dbContext.NotificationPreferences
            .AsNoTracking()
            .Where(p => recipientIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        var now = Now;
        var sends = new List<Send>();
        foreach (var row in rows)
        {
            if (!notifications.TryGetValue(row.NotificationId, out var notification))
            {
                Skip(row, "the notification was deleted", report);
            }
            else if (row.CreatedAt < now - Options.MaxAge)
            {
                Skip(row, $"stale: still waiting after {Options.MaxAge}", report);
            }
            // A token signed into by someone else since must not get this user's notifications.
            else if (!devices.TryGetValue(row.DeviceId, out var device) || device.UserId != notification.UserId)
            {
                Skip(row, "the device is no longer registered to the recipient", report);
            }
            else if (NotificationGroups.For(notification.Type) is var pushGroup
                     && preferences.TryGetValue(notification.UserId, out var preference) && !preference.AllowsPush(pushGroup))
            {
                Skip(row, $"the recipient switched off {pushGroup} notifications", report);
            }
            else
            {
                sends.Add(new Send(row, notification, device));
            }
        }
        if (sends.Count == 0)
        {
            return sends;
        }

        var sentNotifications = sends.Select(s => s.Notification).DistinctBy(n => n.Id).ToList();
        var targets = await targetResolver.ResolveAsync(sentNotifications, cancellationToken);
        var recipients = sentNotifications.Select(n => n.UserId).Distinct().ToList();
        var unreadCounts = await dbContext.Notifications
            .Where(n => recipients.Contains(n.UserId) && !n.IsRead)
            .GroupBy(n => n.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, cancellationToken);

        foreach (var send in sends)
        {
            var n = send.Notification;
            send.Message = PushMessages.Build(n, targets[n.Id], unreadCounts.GetValueOrDefault(n.UserId), send.Device.Token);
        }
        return sends;
    }

    private async Task<PushSendResult[]> SendAllAsync(List<Send> sends, CancellationToken cancellationToken)
    {
        var results = new PushSendResult[sends.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, sends.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Options.MaxParallelSends, CancellationToken = cancellationToken },
            async (i, token) => results[i] = await SendOneAsync(sends[i].Message!, token));
        return results;
    }

    private async Task<PushSendResult> SendOneAsync(PushMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await pushService.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new(PushSendOutcome.RetryLater, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task RecordAsync(List<Send> sends, PushSendResult[] results, BatchReport report, CancellationToken cancellationToken)
    {
        var deadDevices = new HashSet<Guid>();
        for (var i = 0; i < sends.Count; i++)
        {
            var row = sends[i].Row;
            var result = results[i];
            switch (result.Outcome)
            {
                case PushSendOutcome.Sent:
                    Finish(row, PushOutboxStatus.Sent, null);
                    report.Sent++;
                    break;
                case PushSendOutcome.InvalidToken:
                    Finish(row, PushOutboxStatus.Failed, $"invalid token, device removed ({result.Error})");
                    deadDevices.Add(row.DeviceId);
                    break;
                case PushSendOutcome.RetryLater when row.Attempts < Options.MaxAttempts:
                    row.NextAttemptAt = Now + RetryDelay(row.Attempts, result.RetryAfter);
                    row.LastError = Shorten(result.Error);
                    report.Add(report.Retrying, result.Error, row);
                    break;
                case PushSendOutcome.RetryLater:
                    Finish(row, PushOutboxStatus.Failed, $"gave up after {row.Attempts} attempts: {result.Error}");
                    report.Add(report.Failed, $"gave up after {row.Attempts} attempts: {result.Error}", row);
                    break;
                default:
                    Finish(row, PushOutboxStatus.Failed, result.Error);
                    report.Add(report.Failed, result.Error, row);
                    break;
            }
        }

        if (deadDevices.Count > 0)
        {
            var ids = deadDevices.ToList();
            report.DevicesRemoved = await dbContext.UserDevices.Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Exponential backoff from <see cref="PushDeliveryOptions.BaseRetryDelay"/> (attempt 1 waits the base), capped,
    /// plus up to 20% so pushes that failed together don't all come back together; never sooner than Retry-After.
    /// </summary>
    internal TimeSpan RetryDelay(int attempts, TimeSpan? retryAfter)
    {
        var doublings = Math.Clamp(attempts - 1, 0, 30);
        var backoff = TimeSpan.FromTicks((long)Math.Min(Options.BaseRetryDelay.Ticks * Math.Pow(2, doublings), Options.MaxRetryDelay.Ticks));
        var delay = backoff + TimeSpan.FromTicks((long)(backoff.Ticks * 0.2 * Random.Shared.NextDouble()));
        return retryAfter > delay ? retryAfter.Value : delay;
    }

    private void Skip(PushOutboxMessage row, string reason, BatchReport report)
    {
        Finish(row, PushOutboxStatus.Skipped, reason);
        report.Skipped++;
        logger.LogDebug("Skipped push {PushId} for notification {NotificationId}: {Reason}", row.Id, row.NotificationId, reason);
    }

    private void Finish(PushOutboxMessage row, PushOutboxStatus status, string? error)
    {
        row.Status = status;
        row.CompletedAt = Now;
        row.LastError = Shorten(error);
    }

    private static string? Shorten(string? text) =>
        text is null || text.Length <= PushOutboxMessage.LastErrorMaxLength ? text : text[..PushOutboxMessage.LastErrorMaxLength];

    private sealed class Send(PushOutboxMessage row, Notification notification, UserDevice device)
    {
        public PushOutboxMessage Row { get; } = row;
        public Notification Notification { get; } = notification;
        public UserDevice Device { get; } = device;
        public PushMessage? Message { get; set; }
    }

    /// <summary>What a batch did, logged as one summary line plus one warning per distinct error.</summary>
    private sealed class BatchReport
    {
        public int Sent;
        public int Skipped;
        public int DevicesRemoved;
        public readonly Dictionary<string, (int Count, PushOutboxMessage Example)> Retrying = new();
        public readonly Dictionary<string, (int Count, PushOutboxMessage Example)> Failed = new();

        public void Add(Dictionary<string, (int Count, PushOutboxMessage Example)> errors, string? error, PushOutboxMessage row)
        {
            var key = error ?? "unknown error";
            errors[key] = errors.TryGetValue(key, out var seen) ? (seen.Count + 1, seen.Example) : (1, row);
        }

        public void Log(ILogger logger)
        {
            foreach (var (error, (count, example)) in Failed)
            {
                logger.LogWarning("{Count} push notification(s) failed and won't be retried: {Error} (e.g. push {PushId} for notification {NotificationId})",
                    count, error, example.Id, example.NotificationId);
            }
            foreach (var (error, (count, example)) in Retrying)
            {
                logger.LogWarning("{Count} push notification(s) failed and will be retried: {Error} (e.g. push {PushId}, attempt {Attempt})",
                    count, error, example.Id, example.Attempts);
            }
            logger.LogInformation(
                "Push batch: {Sent} sent, {Retrying} to retry, {Failed} failed, {Skipped} skipped, {DevicesRemoved} dead device tokens removed",
                Sent, Retrying.Values.Sum(e => e.Count), Failed.Values.Sum(e => e.Count), Skipped, DevicesRemoved);
        }
    }
}
