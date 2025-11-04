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
    
    // Comment location (ONE will be set)
    public Guid? ChapterId { get; set; }
    public Chapter? Chapter { get; set; }
    
    // NEW: Paragraph-level comments
    public Guid? ParagraphId { get; set; }
    public ChapterParagraph? Paragraph { get; set; }

    public int LikesCount { get; set; } = 0;
    public ICollection<CommentLikes> Likes { get; set; } = new List<CommentLikes>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
    
    // Helper properties
    public bool IsReply => ParentCommentId.HasValue;
    public bool IsChapterComment => ChapterId.HasValue && !ParagraphId.HasValue;
    public bool IsParagraphComment => ParagraphId.HasValue;

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
