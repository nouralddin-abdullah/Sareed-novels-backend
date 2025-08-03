namespace Application.Reviews.DTO;

public class ReviewsDTO
{
    public ReviewerDTO Reviewer { get; set; } = default!;
    public Guid Id { get; set; }
    public decimal TotalAverageScore { get; set; }
    public string? Content { get; set;  }
    public bool IsSpoiler { get; set; }
    public int LikeCount { get; set; }
    public bool IsLikedByCurrentUser { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ReviewerDTO
{
    public string Id { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
}
