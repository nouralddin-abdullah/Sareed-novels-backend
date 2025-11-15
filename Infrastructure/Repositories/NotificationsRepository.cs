using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class NotificationsRepository(ApplicationDbContext dbContext) : INotificationsRepository
{
    public async Task<Notification> CreateNotification(Notification notification)
    {
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();
        return notification;
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
