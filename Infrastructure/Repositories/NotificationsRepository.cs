using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Infrastructure.Push;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>
/// Every notification is created here, and each one also queues a push to each of its recipient's devices
/// (<see cref="PushOutboxMessage"/>), saved in the same transaction; recipients without the app get none.
/// </summary>
public class NotificationsRepository(ApplicationDbContext dbContext, PushOutboxSignal? pushSignal = null) : INotificationsRepository
{
    /// <summary>Notifications inserted per save in a fan-out (a new chapter to every reader of a novel).</summary>
    internal const int FanOutBatchSize = 500;

    public async Task<Notification> CreateNotification(Notification notification)
    {
        dbContext.Notifications.Add(notification);
        var pushes = await PushesFor([notification]);
        dbContext.PushOutbox.AddRange(pushes);
        await dbContext.SaveChangesAsync();
        WakePushWorker(pushes.Count);
        return notification;
    }

    public async Task CreateNotifications(IReadOnlyCollection<Notification> notifications)
    {
        var queued = 0;
        foreach (var batch in notifications.Chunk(FanOutBatchSize))
        {
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

    public async Task<int> GetCommentPageNumber(Guid? chapterId, Guid? postId, Guid commentId, int pageSize)
    {
        IQueryable<Comments> query = dbContext.Comments
            .Where(c => !c.IsDeleted && !c.ParentCommentId.HasValue);

        if (chapterId.HasValue)
        {
            query = query.Where(c => c.ChapterId == chapterId || c.ParagraphId.HasValue && 
                                     dbContext.ChapterParagraphs.Any(p => p.Id == c.ParagraphId && p.ChapterId == chapterId));
        }
        else if (postId.HasValue)
        {
            query = query.Where(c => c.PostId == postId);
        }
        else
        {
            return 1; // Default to first page if no context
        }

        // Count comments created after the target comment (for descending sort)
        var commentsAfter = await query
            .Where(c => c.CreatedAt > dbContext.Comments
                .Where(target => target.Id == commentId)
                .Select(target => target.CreatedAt)
                .FirstOrDefault())
            .CountAsync();

        var pageNumber = (commentsAfter / pageSize) + 1;
        return pageNumber;
    }
}
