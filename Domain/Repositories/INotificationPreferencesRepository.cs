using Domain.Entities;

namespace Domain.Repositories;

public interface INotificationPreferencesRepository
{
    /// <summary>The user's preferences, or the defaults (everything on) when they never changed them.</summary>
    Task<NotificationPreferences> Get(string userId);

    /// <summary>Sets the given groups (null leaves one unchanged) and returns the result. Safe under concurrent calls.</summary>
    Task<NotificationPreferences> Update(string userId, bool? social, bool? chapters, bool? support);
}
