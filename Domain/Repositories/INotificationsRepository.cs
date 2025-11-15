using Domain.Entities;

namespace Domain.Repositories;

public interface INotificationsRepository
{
    Task<Notification> CreateNotification(Notification notification);
    Task<(IEnumerable<Notification>, int)> GetUserNotifications(string userId, int pageNumber, int pageSize, bool unreadOnly = false);
    Task<Notification?> GetNotificationById(Guid notificationId);
    Task<int> GetUnreadCount(string userId);
    Task<bool> MarkAsRead(Guid notificationId);
    Task<bool> MarkAllAsRead(string userId);
    Task<bool> DeleteNotification(Guid notificationId);
    Task<int> GetCommentPageNumber(Guid? chapterId, Guid? postId, Guid commentId, int pageSize);
}
