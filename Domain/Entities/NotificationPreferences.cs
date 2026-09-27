using Domain.Constants;

namespace Domain.Entities;

/// <summary>
/// Which groups of notifications (<see cref="NotificationGroups"/>) a user wants as push notifications. A user
/// without a row gets them all. The in-app notification list is never affected.
/// </summary>
public class NotificationPreferences
{
    public string UserId { get; set; } = default!;
    public bool Social { get; set; } = true;
    public bool Chapters { get; set; } = true;
    public bool Support { get; set; } = true;
    public DateTime UpdatedAt { get; set; }

    public bool AllowsPush(string group) => group switch
    {
        NotificationGroups.Chapters => Chapters,
        NotificationGroups.Support => Support,
        _ => Social
    };
}
