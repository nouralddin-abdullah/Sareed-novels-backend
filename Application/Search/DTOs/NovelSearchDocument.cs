namespace Application.Search.DTOs;

public class NovelSearchDocument
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public string? CoverImageUrl { get; set; }
    public string Status { get; set; } = default!; // "Ongoing", "Completed"
    public bool IsDraft { get; set; }
    public bool IsEligibleForRanking { get; set; }
    
    // Genres (for filtering)
    public List<string> Genres { get; set; } = new();
    
    // Stats (for sorting and filtering)
    public int ChapterCount { get; set; }
    public decimal TotalAverageScore { get; set; }
    public int ReviewCount { get; set; }
    public int TotalViews { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}
