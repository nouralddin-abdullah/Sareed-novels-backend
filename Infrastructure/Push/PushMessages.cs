using System.Globalization;
using Domain.Constants;
using Domain.Entities;

namespace Infrastructure.Push;

/// <summary>
/// The ids a push carries so the mobile app can open the right screen without parsing web URLs. Resolved from
/// the notification's actor and related entity when the push is sent (<see cref="PushTargetResolver"/>).
/// </summary>
public sealed record PushTarget
{
    public string? ActorUserName { get; init; }
    public Guid? NovelId { get; init; }
    public string? NovelSlug { get; init; }
    public Guid? ChapterId { get; init; }
    public Guid? ParagraphId { get; init; }
    /// <summary>The comment the notification is about (for a reply: the reply).</summary>
    public Guid? CommentId { get; init; }
    /// <summary>For a reply (or a like on one): the top-level comment of the thread.</summary>
    public Guid? ParentCommentId { get; init; }
    public Guid? PostId { get; init; }
    public Guid? ReviewId { get; init; }
    public Guid? ReadingListId { get; init; }
}

/// <summary>How a notification looks as a push: Arabic title per type, its message as the body, channel and data.</summary>
public static class PushMessages
{
    /// <summary>Every key is always present (empty when it doesn't apply), and every value is a string.</summary>
    public static readonly IReadOnlyList<string> DataKeys =
    [
        "notificationId", "type", "relatedEntityId", "relatedEntityType", "actorId", "actorUserName",
        "novelId", "novelSlug", "chapterId", "paragraphId", "commentId", "parentCommentId", "postId", "reviewId",
        "readingListId", "unreadCount"
    ];

    // apns-collapse-id may be at most 64 bytes.
    private const int CollapseKeyMaxLength = 64;

    public static PushMessage Build(Notification notification, PushTarget target, int unreadCount, string deviceToken) => new(
        deviceToken,
        TitleFor(notification.Type),
        notification.Message,
        NotificationGroups.For(notification.Type),
        CollapseKeyFor(notification, target),
        unreadCount,
        DataFor(notification, target, unreadCount));

    public static string TitleFor(string notificationType) => notificationType switch
    {
        NotificationType.NewFollower => "متابع جديد",
        NotificationType.CommentOnChapter or NotificationType.CommentOnPost => "تعليق جديد",
        NotificationType.ReplyToComment => "ردّ جديد على تعليقك",
        NotificationType.NewChapterInLibrary => "فصل جديد",
        NotificationType.ReviewOnNovel => "تقييم جديد لروايتك",
        NotificationType.LikeOnPost or NotificationType.LikeOnComment or NotificationType.LikeOnReview => "إعجاب جديد",
        NotificationType.ReadingListFollowed => "متابع جديد لقائمتك",
        NotificationType.GiftReceived => "هدية جديدة",
        NotificationType.PrivilegeSubscribed => "اشتراك جديد",
        _ => "إشعار جديد"
    };

    /// <summary>
    /// Type + what it is about, so ten likes on one comment replace each other instead of stacking: new followers
    /// collapse into one, replies per thread, comments per chapter or post, chapters/reviews/gifts per novel.
    /// </summary>
    public static string CollapseKeyFor(Notification notification, PushTarget target)
    {
        var about = notification.Type switch
        {
            NotificationType.NewFollower => "",
            NotificationType.ReplyToComment => Id(target.ParentCommentId ?? target.CommentId),
            NotificationType.CommentOnChapter => Id(target.ChapterId),
            NotificationType.CommentOnPost => Id(target.PostId),
            NotificationType.NewChapterInLibrary or NotificationType.ReviewOnNovel
                or NotificationType.GiftReceived or NotificationType.PrivilegeSubscribed => Id(target.NovelId),
            _ => Id(notification.RelatedEntityId)
        };
        if (about.Length == 0 && notification.Type != NotificationType.NewFollower)
        {
            about = notification.Id.ToString(); // don't collapse what we can't tell apart
        }

        var type = notification.Type;
        var room = CollapseKeyMaxLength - (about.Length == 0 ? 0 : about.Length + 1);
        if (type.Length > room)
        {
            type = type[..Math.Max(room, 0)];
        }
        return about.Length == 0 ? type : $"{type}:{about}";
    }

    public static IReadOnlyDictionary<string, string> DataFor(Notification notification, PushTarget target, int unreadCount) =>
        new Dictionary<string, string>
        {
            ["notificationId"] = notification.Id.ToString(),
            ["type"] = notification.Type,
            ["relatedEntityId"] = Id(notification.RelatedEntityId),
            ["relatedEntityType"] = notification.RelatedEntityType ?? "",
            ["actorId"] = notification.ActorId,
            ["actorUserName"] = target.ActorUserName ?? "",
            ["novelId"] = Id(target.NovelId),
            ["novelSlug"] = target.NovelSlug ?? "",
            ["chapterId"] = Id(target.ChapterId),
            ["paragraphId"] = Id(target.ParagraphId),
            ["commentId"] = Id(target.CommentId),
            ["parentCommentId"] = Id(target.ParentCommentId),
            ["postId"] = Id(target.PostId),
            ["reviewId"] = Id(target.ReviewId),
            ["readingListId"] = Id(target.ReadingListId),
            ["unreadCount"] = unreadCount.ToString(CultureInfo.InvariantCulture)
        };

    private static string Id(Guid? id) => id?.ToString() ?? "";
}
