namespace Application.Notifications.DTOs;

public class NotificationDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = default!;
    public string ActorId { get; set; } = default!;
    public string ActorDisplayName { get; set; } = default!;
    public string? ActorProfilePhoto { get; set; }
    public string Message { get; set; } = default!;
    public string ActionUrl { get; set; } = default!;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }

    /// <summary>
    /// The novel the notification is about, for GiftReceived, PrivilegeSubscribed, NewChapterInLibrary, ReviewOnNovel
    /// and LikeOnReview (null for the others, and for a review that has since been deleted). Open it with
    /// GET /api/novel/by-id/{novelId}: a rename changes the slug frozen in <see cref="ActionUrl"/>. Comment
    /// notifications get their novel from GET /api/notifications/comment/{RelatedEntityId}.
    /// </summary>
    public Guid? NovelId { get; set; }

    /// <summary>The novel's current slug; null when there is no <see cref="NovelId"/> or the novel was deleted.</summary>
    public string? NovelSlug { get; set; }

    // The parts the message names (#25), so clients needn't read them out of the Arabic sentence. Names are current,
    // like NovelSlug (a rename shows here, while Message keeps the words it was sent with), and null when their thing
    // was deleted since (clients then show Message as it is).

    /// <summary>The current title of <see cref="NovelId"/>'s novel; null when there is none or it was deleted.</summary>
    public string? NovelTitle { get; set; }

    /// <summary>
    /// The chapter the message names: the new chapter of NewChapterInLibrary, and the chapter commented on of
    /// CommentOnChapter; null for the others. <see cref="ChapterTitle"/> is its current title (null once deleted).
    /// </summary>
    public Guid? ChapterId { get; set; }
    public string? ChapterTitle { get; set; }

    /// <summary>The current name of the followed list of ReadingListFollowed (its id is RelatedEntityId); null otherwise.</summary>
    public string? ReadingListName { get; set; }

    /// <summary>
    /// GiftReceived: the gift, its Arabic name (retired gifts included) and how many were sent; null for other types
    /// and for gift notifications from before #25.
    /// </summary>
    public Guid? GiftId { get; set; }
    public string? GiftNameAr { get; set; }
    public int? GiftCount { get; set; }
}

public class NotificationListDto
{
    public List<NotificationDto> Notifications { get; set; } = new();
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class CommentDetailDto
{
    public CommentDto Comment { get; set; } = default!;
    public CommentLocationDto Context { get; set; } = default!;
    public CommentDto? ParentComment { get; set; }
    public List<CommentReplyDto> Replies { get; set; } = new();
}

public class CommentDto
{
    public Guid Id { get; set; }
    public CommentUserDto User { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string? AttachedImageUrl { get; set; }
    public int LikesCount { get; set; }
    public bool IsLikedByCurrentUser { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CommentReplyDto
{
    public Guid Id { get; set; }
    public CommentUserDto User { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string? AttachedImageUrl { get; set; }
    public int LikesCount { get; set; }
    public bool IsLikedByCurrentUser { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CommentUserDto
{
    public string Id { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
}

public class CommentLocationDto
{
    /// <summary>
    /// The page that holds the comment in the list that shows it, at the pageSize asked for (10 by default) and in that
    /// list's default order: for a top-level comment, its paragraph's, chapter's or post's comments (newest first); for a
    /// reply, the replies of <see cref="ParentCommentId"/> (oldest first).
    /// </summary>
    public int PageNumber { get; set; }
    public Guid? ChapterId { get; set; }
    public string? ChapterTitle { get; set; }
    public string? ChapterSlug { get; set; }
    public Guid? PostId { get; set; }
    public Guid? NovelId { get; set; }
    public string? NovelSlug { get; set; }
    public string? NovelTitle { get; set; }
    public int TotalComments { get; set; }

    /// <summary>For a comment on a paragraph (or a reply under one): the paragraph; null otherwise.</summary>
    public Guid? ParagraphId { get; set; }

    /// <summary>That paragraph's position in its chapter, from 0.</summary>
    public int? ParagraphOrderIndex { get; set; }

    /// <summary>
    /// The start of that paragraph as plain text, at most 140 characters; null when the caller may not read the chapter
    /// (a draft, or locked for them by the privilege system), as the chapter reader decides.
    /// </summary>
    public string? ParagraphExcerpt { get; set; }

    /// <summary>For a reply, the top-level comment it answers (its thread holds the reply); null otherwise.</summary>
    public Guid? ParentCommentId { get; set; }
}
