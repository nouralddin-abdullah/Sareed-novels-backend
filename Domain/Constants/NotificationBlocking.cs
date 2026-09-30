namespace Domain.Constants;

/// <summary>
/// Which blocks stop a notification (#10, #52). NotificationsRepository applies this to every notification it creates,
/// pushes included, and creates none that a block stops.
/// </summary>
public static class NotificationBlocking
{
    /// <summary>
    /// True when a block either way stops a notification of <paramref name="notificationType"/>: its recipient blocked
    /// its actor, or its actor blocked its recipient. False for the types only the recipient's block stops. A type not
    /// named here, one added later included, is stopped either way, so none slips through.
    /// </summary>
    public static bool EitherWay(string notificationType) => notificationType switch
    {
        // A new chapter goes to every reader who keeps the novel in their library; its actor is the novel.
        NotificationType.NewChapterInLibrary => false,
        // Payments: an author learns of them, even from someone who blocked them.
        NotificationType.GiftReceived or NotificationType.PrivilegeSubscribed => false,
        // From one member to another: follows, comments, replies, reviews, likes and list follows.
        _ => true
    };
}
