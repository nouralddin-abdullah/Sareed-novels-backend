namespace Application.Posts.DTOs;

public class PostDTO
{
    public Guid Id { get; set; }
    public PostUserDTO User { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string? ImageUrl { get; set; }
    public PostNovelDTO? Novel { get; set; }
    public DateTime CreatedAt { get; set; }
    public int LikesCount { get; set; }
    public int CommentsCount { get; set; }
    public bool IsLikedByCurrentUser { get; set; }
}

public class PostUserDTO
{
    public string Id { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
}

public class PostNovelDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public decimal TotalAverageScore { get; set; }
    public int ReviewCount { get; set; }
}
