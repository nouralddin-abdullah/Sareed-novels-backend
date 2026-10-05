namespace Domain.Constants;

public static class NotificationType
{
    public const string NewFollower = "NewFollower";
    public const string CommentOnChapter = "CommentOnChapter";
    public const string CommentOnPost = "CommentOnPost";
    public const string ReplyToComment = "ReplyToComment";
    public const string NewChapterInLibrary = "NewChapterInLibrary";
    public const string ReviewOnNovel = "ReviewOnNovel";
    public const string GiftReceived = "GiftReceived";
    public const string PrivilegeSubscribed = "PrivilegeSubscribed"; // ✅ NEW: Author notified when someone subscribes
    
    // Phase 2 (Optional)
    public const string LikeOnReview = "LikeOnReview";
    public const string LikeOnComment = "LikeOnComment";
    public const string LikeOnPost = "LikeOnPost";
    public const string ReadingListFollowed = "ReadingListFollowed";

    /// <summary>
    /// Every type above: the names the types filter of GET /api/notifications accepts (#78). A unit test fails when a
    /// type is added above and not here.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        NewFollower, CommentOnChapter, CommentOnPost, ReplyToComment, NewChapterInLibrary, ReviewOnNovel, GiftReceived,
        PrivilegeSubscribed, LikeOnReview, LikeOnComment, LikeOnPost, ReadingListFollowed
    ];
}
