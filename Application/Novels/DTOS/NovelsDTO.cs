using Domain.Entities;

namespace Application.Novels.DTOS;

public class NovelsDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public string Status { get; set; } = default!;
    public int TotalViews { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal AverageWritingQualityScore { get; set; }
    public decimal AverageUpdatingStabilityScore { get; set; }
    public decimal AverageCharacterDevelopmentScore { get; set; }
    public decimal AverageWorldBuildingScore { get; set; }
    public decimal TotalAverageScore { get; set; }
    public int ReviewCount { get; set; }
    public AuthorDTO Author { get; set; } = default!;
}

public class AuthorDTO
{
    public string Id { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string ProfilePhoto { get; set; } = default!;
}
