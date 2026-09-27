using Domain.Entities;

namespace Application.Notifications.DTOs;

/// <summary>Which groups of notifications are sent as push notifications; the in-app list always has everything.</summary>
public class NotificationPreferencesDto
{
    /// <summary>Replies, comments, likes, reviews and follows.</summary>
    public bool Social { get; set; }

    /// <summary>New chapters of novels in the library.</summary>
    public bool Chapters { get; set; }

    /// <summary>Gifts and privilege subscriptions (for authors).</summary>
    public bool Support { get; set; }

    public static NotificationPreferencesDto From(NotificationPreferences preferences) => new()
    {
        Social = preferences.Social,
        Chapters = preferences.Chapters,
        Support = preferences.Support
    };
}
