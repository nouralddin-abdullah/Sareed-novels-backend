using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Infrastructure.Push;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>
/// Every notification is created here, and each one also queues a push to each of its recipient's devices
/// (<see cref="PushOutboxMessage"/>), saved in the same transaction; recipients without the app get none. This is also
/// where blocks apply to notifications (<see cref="NotificationBlocking"/>): none is created, so no push either, when
/// its recipient blocked its actor, or, for a notification from one member to another, when its actor blocked its
/// recipient.
/// </summary>
public class NotificationsRepository(ApplicationDbContext dbContext, PushOutboxSignal? pushSignal = null) : INotificationsRepository
{
    /// <summary>Notifications inserted per save in a fan-out (a new chapter to every reader of a novel).</summary>
    internal const int FanOutBatchSize = 500;

    public async Task<bool> CreateNotification(Notification notification)
    {
        if (await IsStoppedByABlock(notification))
        {
            return false;
        }

        dbContext.Notifications.Add(notification);
        var pushes = await PushesFor([notification]);
        dbContext.PushOutbox.AddRange(pushes);
        await dbContext.SaveChangesAsync();
        WakePushWorker(pushes.Count);
        return true;
    }

    public async Task CreateNotifications(IReadOnlyCollection<Notification> notifications)
    {
        var queued = 0;
        foreach (var chunk in notifications.Chunk(FanOutBatchSize))
        {
            var batch = await NotStoppedByABlock(chunk);
            if (batch.Count == 0)
            {
                continue;
            }

            dbContext.Notifications.AddRange(batch);
            var pushes = await PushesFor(batch);
            dbContext.PushOutbox.AddRange(pushes);
            await dbContext.SaveChangesAsync();

            // Keep the change tracker small over thousands of readers.
            foreach (var entity in batch.Cast<object>().Concat(pushes))
            {
                dbContext.Entry(entity).State = EntityState.Detached;
            }
            queued += pushes.Count;
        }
        WakePushWorker(queued);
    }

    public async Task<bool> CreateUnlessUnreadExists(Notification notification)
    {
        var n = notification;
        var eitherWay = NotificationBlocking.EitherWay(n.Type);
        var pushes = await PushesFor([n]);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Notifications (Id, UserId, Type, ActorId, ActorDisplayName, ActorProfilePhoto, Message, ActionUrl,
                                       IsRead, CreatedAt, RelatedEntityId, RelatedEntityType)
            SELECT {n.Id}, {n.UserId}, {n.Type}, {n.ActorId}, {n.ActorDisplayName}, {n.ActorProfilePhoto}, {n.Message},
                   {n.ActionUrl}, 0, {n.CreatedAt}, {n.RelatedEntityId}, {n.RelatedEntityType}
            WHERE NOT EXISTS (
                SELECT 1 FROM Notifications WITH (UPDLOCK, HOLDLOCK)
                WHERE UserId = {n.UserId} AND IsRead = 0 AND Type = {n.Type} AND ActorId = {n.ActorId}
                  AND (RelatedEntityId = {n.RelatedEntityId} OR (RelatedEntityId IS NULL AND {n.RelatedEntityId} IS NULL)))
              AND NOT EXISTS (
                SELECT 1 FROM UserBlocks
                WHERE (BlockerId = {n.UserId} AND BlockedId = {n.ActorId})
                   OR ({eitherWay} = 1 AND BlockerId = {n.ActorId} AND BlockedId = {n.UserId}))
            """);
        if (inserted == 1 && pushes.Count > 0)
        {
            dbContext.PushOutbox.AddRange(pushes);
            await dbContext.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        if (inserted == 1)
        {
            WakePushWorker(pushes.Count);
        }
        return inserted == 1;
    }

    /// <summary>
    /// Whether a block stops the notification (<see cref="NotificationBlocking"/>): its recipient blocked its actor,
    /// or, for the types a block either way stops, its actor blocked its recipient.
    /// </summary>
    private Task<bool> IsStoppedByABlock(Notification n) => NotificationBlocking.EitherWay(n.Type)
        ? dbContext.UserBlocks.AnyAsync(b => (b.BlockerId == n.UserId && b.BlockedId == n.ActorId)
                                             || (b.BlockerId == n.ActorId && b.BlockedId == n.UserId))
        : dbContext.UserBlocks.AnyAsync(b => b.BlockerId == n.UserId && b.BlockedId == n.ActorId);

    /// <summary>
    /// The notifications no block stops (<see cref="IsStoppedByABlock"/>), with one query for the batch: the blocks
    /// between its recipients and its actors, either way.
    /// </summary>
    private async Task<List<Notification>> NotStoppedByABlock(IReadOnlyCollection<Notification> notifications)
    {
        var recipientIds = notifications.Select(n => n.UserId).Distinct().ToList();
        var actorIds = notifications.Select(n => n.ActorId).Distinct().ToList();
        var blocks = await dbContext.UserBlocks
            .AsNoTracking()
            .Where(b => (recipientIds.Contains(b.BlockerId) && actorIds.Contains(b.BlockedId))
                        || (actorIds.Contains(b.BlockerId) && recipientIds.Contains(b.BlockedId)))
            .Select(b => new { b.BlockerId, b.BlockedId })
            .ToListAsync();
        if (blocks.Count == 0)
        {
            return notifications.ToList();
        }

        var blocked = blocks.Select(b => (b.BlockerId, b.BlockedId)).ToHashSet();
        return notifications
            .Where(n => !blocked.Contains((n.UserId, n.ActorId))
                        && !(NotificationBlocking.EitherWay(n.Type) && blocked.Contains((n.ActorId, n.UserId))))
            .ToList();
    }

    /// <summary>One outbox row per device of each notification's recipient (not yet added to the context).</summary>
    private async Task<List<PushOutboxMessage>> PushesFor(IReadOnlyCollection<Notification> notifications)
    {
        var userIds = notifications.Select(n => n.UserId).Distinct().ToList();
        var devices = await dbContext.UserDevices
            .AsNoTracking()
            .Where(d => userIds.Contains(d.UserId))
            .Select(d => new { d.Id, d.UserId })
            .ToListAsync();
        if (devices.Count == 0)
        {
            return [];
        }

        var devicesByUser = devices.ToLookup(d => d.UserId, d => d.Id);
        return notifications
            .SelectMany(n => devicesByUser[n.UserId].Select(deviceId => PushOutboxMessage.For(n, deviceId)))
            .ToList();
    }

    private void WakePushWorker(int queued)
    {
        if (queued > 0)
        {
            pushSignal?.Notify();
        }
    }

    public async Task<(IEnumerable<Notification>, int)> GetUserNotifications(string userId, int pageNumber, int pageSize, bool unreadOnly = false)
    {
        IQueryable<Notification> query = dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        var totalCount = await query.CountAsync();

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (notifications, totalCount);
    }

    public async Task<Notification?> GetNotificationById(Guid notificationId)
    {
        return await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId);
    }

    public async Task<int> GetUnreadCount(string userId)
    {
        return await dbContext.Notifications
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<bool> MarkAsRead(Guid notificationId) =>
        // One statement; SQL Server counts the row even when it was already read, so a repeat (or a race) succeeds.
        await dbContext.Notifications
            .Where(n => n.Id == notificationId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true)) > 0;

    public Task<int> MarkAllAsRead(string userId) =>
        // One statement instead of loading and saving every unread row; nothing left to mark is not a failure.
        dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

    public async Task<bool> DeleteNotification(Guid notificationId)
    {
        var notification = await dbContext.Notifications.FindAsync(notificationId);
        if (notification == null) return false;

        dbContext.Notifications.Remove(notification);
        return await dbContext.SaveChangesAsync() > 0;
    }
}
