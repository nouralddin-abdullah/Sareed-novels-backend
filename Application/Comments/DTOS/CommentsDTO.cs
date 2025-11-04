using Domain.Entities;

namespace Application.Comments.DTOS;

public class CommentsDTO
{
    public Guid Id { get; set; }
    public CommentUserDTO User { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string? AttachedImageUrl { get; set; }
    public Guid? ParentCommentId { get; set; }
    public Guid? ChapterId { get; set; }
    public int LikesCount { get; set; } 
    public bool IsLikedByCurrentUser { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public int? TotalRepliesCount { get; set; }
    public bool? HasMoreReplies { get; set; }
}
public class CommentReplyDTO
{
    public Guid Id { get; set; }
    public CommentUserDTO User { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string? AttachedImageUrl { get; set; }
    public int LikesCount { get; set; }
    public bool IsLikedByCurrentUser { get; set; } = false;
    public DateTime CreatedAt { get; set; }
}


public class CommentUserDTO
{
    public string Id { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
}