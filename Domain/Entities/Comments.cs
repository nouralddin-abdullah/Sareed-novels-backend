namespace Domain.Entities;

public class Comments
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string? AttachedImageUrl { get; set; }

    public Guid? ParentCommentId { get; set; }
    public Comments? ParentComment { get; set; }
    public ICollection<Comments> Replies { get; set; } = new List<Comments>();
    public Guid? ChapterId { get; set; }
    public Chapter? Chapter { get; set; }

    public int LikesCount { get; set; } = 0;
    public ICollection<CommentLikes> Likes { get; set; } = new List<CommentLikes>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
    public bool IsReply => ParentCommentId.HasValue;

    public void IncrementLikeCount()
    {
        LikesCount++;
    }

    public void DecrementLikeCount()
    {
        if (LikesCount > 0)
        {
            LikesCount--;
        }
    }
}
