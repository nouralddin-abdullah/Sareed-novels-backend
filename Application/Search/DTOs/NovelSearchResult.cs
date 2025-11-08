namespace Application.Search.DTOs;

public class NovelSearchResult
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public string? CoverImageUrl { get; set; }
    public string Status { get; set; } = default!;
    public List<string> Genres { get; set; } = new();
    public int ChapterCount { get; set; }
    public decimal TotalAverageScore { get; set; }
    public int ReviewCount { get; set; }
    public int TotalViews { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}
