namespace Domain.Entities;

public class Post
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string? ImageUrl { get; set; }
    public Guid? NovelId { get; set; }
    public Novel? Novel { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int LikesCount { get; set; } = 0;
    public int CommentsCount { get; set; } = 0;
    public bool IsDeleted { get; set; } = false;

    public ICollection<PostLike> Likes { get; set; } = new List<PostLike>();
    public ICollection<Comments> Comments { get; set; } = new List<Comments>();

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

    public void IncrementCommentsCount()
    {
        CommentsCount++;
    }

    public void DecrementCommentsCount()
    {
        if (CommentsCount > 0)
        {
            CommentsCount--;
        }
    }
}
