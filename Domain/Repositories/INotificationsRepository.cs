using Domain.Entities;

namespace Domain.Repositories;

public interface INotificationsRepository
{
    Task<Notification> CreateNotification(Notification notification);
    /// <summary>Inserts many notifications (a fan-out) in batches rather than one round trip each.</summary>
    Task CreateNotifications(IReadOnlyCollection<Notification> notifications);
    /// <summary>
    /// Inserts the notification unless its recipient still has an unread one of the same type from the same actor
    /// about the same item (RelatedEntityId); false when it was skipped. Safe under concurrent calls.
    /// </summary>
    Task<bool> CreateUnlessUnreadExists(Notification notification);
    Task<(IEnumerable<Notification>, int)> GetUserNotifications(string userId, int pageNumber, int pageSize, bool unreadOnly = false);
    Task<Notification?> GetNotificationById(Guid notificationId);
    Task<int> GetUnreadCount(string userId);
    Task<bool> MarkAsRead(Guid notificationId);
    Task<bool> MarkAllAsRead(string userId);
    Task<bool> DeleteNotification(Guid notificationId);
}
