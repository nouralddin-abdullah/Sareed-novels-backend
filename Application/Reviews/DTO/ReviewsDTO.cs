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

public class NovelReviewsResponse
{
    public List<ReviewsDTO> Reviews { get; set; } = new();
    public CurrentUserReviewDTO? CurrentUserReview { get; set; }
    public int TotalCount { get; set; }
    public int PageSize { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
}

public class CurrentUserReviewDTO
{
    public Guid Id { get; set; }
    public decimal WritingQualityScore { get; set; }
    public decimal UpdatingStabilityScore { get; set; }
    public decimal CharacterDevelopmentScore { get; set; }
    public decimal WorldBuildingScore { get; set; }
    public decimal TotalAverageScore { get; set; }
    public string? Content { get; set; }
    public bool IsSpoiler { get; set; }
    public int LikeCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ReviewerDTO
{
    public string Id { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
}
