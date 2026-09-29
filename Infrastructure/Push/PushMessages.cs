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

    /// <summary>
    /// The user name of the post's author when the notification is about a post or a comment on one (a reply or like
    /// under someone else's post included), since the app opens a post from its author's profile.
    /// </summary>
    public string? PostAuthorUserName { get; init; }

    public Guid? ReviewId { get; init; }
    public Guid? ReadingListId { get; init; }

    /// <summary>
    /// Not an id and not in the data: what a gift's sender wrote (#31), which the push body quotes under the sentence;
    /// null when there is none or a moderator removed it.
    /// </summary>
    public string? GiftMessage { get; init; }
}

/// <summary>How a notification looks as a push: Arabic title per type, its message as the body, channel and data.</summary>
public static class PushMessages
{
    /// <summary>How much of a gift's message a push quotes, in user-perceived characters, «…» included.</summary>
    public const int GiftMessageExcerptLength = 100;

    /// <summary>
    /// And at most this many UTF-16 units (a hundred emoji can take well over a thousand), so the body stays far from
    /// FCM's 4 KB limit for a whole message.
    /// </summary>
    internal const int GiftMessageExcerptMaxUnits = 300;

    /// <summary>
    /// The keys of a push's data. Every key is always present (empty when it doesn't apply), and every value is a
    /// string. <c>notificationId</c>, <c>type</c>, <c>relatedEntityId</c>, <c>relatedEntityType</c> and
    /// <c>actorId</c> are the notification's own; <c>unreadCount</c> is the recipient's unread notifications. The
    /// rest are what the app opens (<see cref="PushTarget"/>): <c>actorUserName</c> (a follower's or commenter's
    /// profile), <c>novelId</c> and <c>novelSlug</c>, <c>chapterId</c>, <c>paragraphId</c>, <c>commentId</c> (for a
    /// reply: the reply) and <c>parentCommentId</c> (its thread), <c>postId</c> with <c>postAuthorUserName</c> (the
    /// post's author, whose profile the post is opened from: for a comment on a post, a reply under one or a like on
    /// either, also when the post is someone else's), <c>reviewId</c> and <c>readingListId</c>. Keys may be added
    /// later: a client ignores the ones it doesn't know.
    /// </summary>
    public static readonly IReadOnlyList<string> DataKeys =
    [
        "notificationId", "type", "relatedEntityId", "relatedEntityType", "actorId", "actorUserName",
        "novelId", "novelSlug", "chapterId", "paragraphId", "commentId", "parentCommentId", "postId",
        "postAuthorUserName", "reviewId", "readingListId", "unreadCount"
    ];

    // apns-collapse-id may be at most 64 bytes.
    private const int CollapseKeyMaxLength = 64;

    public static PushMessage Build(Notification notification, PushTarget target, int unreadCount, string deviceToken) => new(
        deviceToken,
        TitleFor(notification.Type),
        BodyFor(notification, target),
        NotificationGroups.For(notification.Type),
        CollapseKeyFor(notification, target),
        unreadCount,
        DataFor(notification, target, unreadCount));

    /// <summary>
    /// The notification's message; for a gift with a message (#31), then a new line with «the message», cut at about
    /// <see cref="GiftMessageExcerptLength"/> characters with «…».
    /// </summary>
    public static string BodyFor(Notification notification, PushTarget target) =>
        notification.Type == NotificationType.GiftReceived && !string.IsNullOrWhiteSpace(target.GiftMessage)
            ? $"{notification.Message}\n«{Excerpt(target.GiftMessage.Trim())}»"
            : notification.Message;

    /// <summary>
    /// <paramref name="text"/> when it fits, else its first whole user-perceived characters (an emoji or a letter with
    /// its marks is never split) and «…», within <see cref="GiftMessageExcerptLength"/> characters and
    /// <see cref="GiftMessageExcerptMaxUnits"/> UTF-16 units.
    /// </summary>
    internal static string Excerpt(string text)
    {
        if (text.Length <= GiftMessageExcerptMaxUnits && new StringInfo(text).LengthInTextElements <= GiftMessageExcerptLength)
        {
            return text;
        }

        // Whole characters while they leave room for the «…».
        var elements = StringInfo.GetTextElementEnumerator(text);
        var (count, end) = (0, 0);
        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            if (count == GiftMessageExcerptLength - 1 || end + element.Length > GiftMessageExcerptMaxUnits - 1)
            {
                break;
            }
            count++;
            end += element.Length;
        }
        return text[..end].TrimEnd() + "…";
    }

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
            ["postAuthorUserName"] = target.PostAuthorUserName ?? "",
            ["reviewId"] = Id(target.ReviewId),
            ["readingListId"] = Id(target.ReadingListId),
            ["unreadCount"] = unreadCount.ToString(CultureInfo.InvariantCulture)
        };

    private static string Id(Guid? id) => id?.ToString() ?? "";
}
