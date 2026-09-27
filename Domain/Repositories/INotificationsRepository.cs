using Domain.Entities;

namespace Domain.Repositories;

/// <summary>
/// Every notification is created through here, and none reaches a recipient who blocked its actor (ActorId): the
/// create methods skip those, pushes included.
/// </summary>
public interface INotificationsRepository
{
    /// <summary>Inserts the notification (and queues its pushes); false when skipped because the recipient blocked the actor.</summary>
    Task<bool> CreateNotification(Notification notification);
    /// <summary>
    /// Inserts many notifications (a fan-out) in batches rather than one round trip each, leaving out recipients who
    /// blocked the actor.
    /// </summary>
    Task CreateNotifications(IReadOnlyCollection<Notification> notifications);
    /// <summary>
    /// Inserts the notification unless its recipient still has an unread one of the same type from the same actor
    /// about the same item (RelatedEntityId), or blocked the actor; false when it was skipped. Safe under concurrent calls.
    /// </summary>
    Task<bool> CreateUnlessUnreadExists(Notification notification);
    Task<(IEnumerable<Notification>, int)> GetUserNotifications(string userId, int pageNumber, int pageSize, bool unreadOnly = false);
    Task<Notification?> GetNotificationById(Guid notificationId);
    Task<int> GetUnreadCount(string userId);
    Task<bool> MarkAsRead(Guid notificationId);
    Task<bool> MarkAllAsRead(string userId);
    Task<bool> DeleteNotification(Guid notificationId);
}
