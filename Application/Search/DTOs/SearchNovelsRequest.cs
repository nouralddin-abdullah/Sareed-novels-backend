namespace Application.Search.DTOs;

public class SearchNovelsRequest
{
    public string? Query { get; set; }
    
    // Filters
    public List<string>? Genres { get; set; }
    public string? Status { get; set; } // "Ongoing", "Completed"
    
    // Chapter count filtering - supports multiple ranges
    public List<ChapterCountRange>? ChapterRanges { get; set; }
    
    // Keep single range for backward compatibility
    public ChapterCountRange? ChapterRange { get; set; }
    
    // Sorting
    public NovelSortBy SortBy { get; set; } = NovelSortBy.Relevance;
    
    // Pagination
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public enum ChapterCountRange
{
    Range_1_10,    // 1-10 chapters
    Range_10_20,   // 10-20 chapters
    Range_20_50,   // 20-50 chapters
    Range_50_Plus  // 50+ chapters
}

public enum NovelSortBy
{
    Relevance,      // Default (by Elasticsearch score)
    Newest,         // CreatedAt DESC
    LastUpdated,    // LastUpdatedAt DESC
    MostPopular,    // TotalViews DESC
    HighestRated,   // TotalAverageScore DESC
    MostReviewed    // ReviewCount DESC
}
