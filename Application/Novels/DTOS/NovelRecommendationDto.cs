namespace Application.Novels.DTOS;

public class NovelRecommendationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public string Status { get; set; } = default!;
    public int TotalViews { get; set; }
    public int ChapterCount { get; set; }
    public decimal SimilarityScore { get; set; }
}



