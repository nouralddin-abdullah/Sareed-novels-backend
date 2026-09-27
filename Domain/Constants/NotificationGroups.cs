namespace Domain.Constants;

/// <summary>
/// Notification types grouped for push: the user switches each group on or off, and each is an Android
/// notification channel with the same id in the mobile app.
/// </summary>
public static class NotificationGroups
{
    /// <summary>Replies, comments, likes, reviews and follows.</summary>
    public const string Social = "social";

    /// <summary>New chapters of novels in the reader's library.</summary>
    public const string Chapters = "chapters";

    /// <summary>Gifts and privilege subscriptions an author receives.</summary>
    public const string Support = "support";

    public static string For(string notificationType) => notificationType switch
    {
        NotificationType.NewChapterInLibrary => Chapters,
        NotificationType.GiftReceived or NotificationType.PrivilegeSubscribed => Support,
        _ => Social
    };
}
