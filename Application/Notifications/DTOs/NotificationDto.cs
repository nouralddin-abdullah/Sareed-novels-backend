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
    public int PageNumber { get; set; }
    public Guid? ChapterId { get; set; }
    public string? ChapterTitle { get; set; }
    public string? ChapterSlug { get; set; }
    public Guid? PostId { get; set; }
    public Guid? NovelId { get; set; }
    public string? NovelSlug { get; set; }
    public string? NovelTitle { get; set; }
    public int TotalComments { get; set; }
}
