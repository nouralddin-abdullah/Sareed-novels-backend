using Domain.Entities;

namespace Domain.Repositories;

/// <summary>
/// Every notification is created through here, and none that a block stops
/// (<see cref="Constants.NotificationBlocking"/>): the create methods skip a notification whose recipient blocked its
/// actor (ActorId), and, for one from one member to another, one whose actor blocked its recipient, pushes included.
/// </summary>
public interface INotificationsRepository
{
    /// <summary>Inserts the notification (and queues its pushes); false when a block stops it.</summary>
    Task<bool> CreateNotification(Notification notification);
    /// <summary>
    /// Inserts many notifications (a fan-out) in batches rather than one round trip each, leaving out those a block
    /// stops (one query per batch).
    /// </summary>
    Task CreateNotifications(IReadOnlyCollection<Notification> notifications);
    /// <summary>
    /// Inserts the notification unless its recipient still has an unread one of the same type from the same actor
    /// about the same item (RelatedEntityId), or a block stops it; false when it was skipped. Safe under concurrent
    /// calls.
    /// </summary>
    Task<bool> CreateUnlessUnreadExists(Notification notification);
    Task<(IEnumerable<Notification>, int)> GetUserNotifications(string userId, int pageNumber, int pageSize, bool unreadOnly = false);
    Task<Notification?> GetNotificationById(Guid notificationId);
    Task<int> GetUnreadCount(string userId);
    /// <summary>Marks the notification read (already read is fine); false when it doesn't exist.</summary>
    Task<bool> MarkAsRead(Guid notificationId);
    /// <summary>Marks every unread notification of the user read in one statement; how many there were (0 is fine).</summary>
    Task<int> MarkAllAsRead(string userId);
    Task<bool> DeleteNotification(Guid notificationId);
}
