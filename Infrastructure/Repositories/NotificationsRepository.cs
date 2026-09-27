using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Infrastructure.Push;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>
/// Every notification is created here, and each one also queues a push to each of its recipient's devices
/// (<see cref="PushOutboxMessage"/>), saved in the same transaction; recipients without the app get none. This is also
/// where blocks apply to notifications: none is created for a recipient who blocked its actor, so no push either.
/// </summary>
public class NotificationsRepository(ApplicationDbContext dbContext, PushOutboxSignal? pushSignal = null) : INotificationsRepository
{
    /// <summary>Notifications inserted per save in a fan-out (a new chapter to every reader of a novel).</summary>
    internal const int FanOutBatchSize = 500;

    public async Task<bool> CreateNotification(Notification notification)
    {
        if (await dbContext.UserBlocks.AnyAsync(b => b.BlockerId == notification.UserId && b.BlockedId == notification.ActorId))
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
            var batch = await WithoutBlockedActors(chunk);
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
              AND NOT EXISTS (SELECT 1 FROM UserBlocks WHERE BlockerId = {n.UserId} AND BlockedId = {n.ActorId})
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

    /// <summary>The notifications whose recipient hasn't blocked their actor, in one query for the batch.</summary>
    private async Task<List<Notification>> WithoutBlockedActors(IReadOnlyCollection<Notification> notifications)
    {
        var recipientIds = notifications.Select(n => n.UserId).Distinct().ToList();
        var actorIds = notifications.Select(n => n.ActorId).Distinct().ToList();
        var blocks = await dbContext.UserBlocks
            .AsNoTracking()
            .Where(b => recipientIds.Contains(b.BlockerId) && actorIds.Contains(b.BlockedId))
            .Select(b => new { b.BlockerId, b.BlockedId })
            .ToListAsync();
        if (blocks.Count == 0)
        {
            return notifications.ToList();
        }

        var blocked = blocks.Select(b => (b.BlockerId, b.BlockedId)).ToHashSet();
        return notifications.Where(n => !blocked.Contains((n.UserId, n.ActorId))).ToList();
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

    public async Task<bool> MarkAsRead(Guid notificationId)
    {
        var notification = await dbContext.Notifications.FindAsync(notificationId);
        if (notification == null) return false;
        
        notification.MarkAsRead();
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> MarkAllAsRead(string userId)
    {
        var unreadNotifications = await dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var notification in unreadNotifications)
        {
            notification.MarkAsRead();
        }

        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteNotification(Guid notificationId)
    {
        var notification = await dbContext.Notifications.FindAsync(notificationId);
        if (notification == null) return false;

        dbContext.Notifications.Remove(notification);
        return await dbContext.SaveChangesAsync() > 0;
    }
}
