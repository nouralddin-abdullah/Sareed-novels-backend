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

    /// <summary>
    /// true: only the novels a reader can open, with at least one published chapter, in the page and in every count
    /// (#58, the rule of withChapters on a member's works, #46). false or left out (null): every novel that isn't a
    /// draft, those without a published chapter too, as before.
    /// </summary>
    public bool? WithChapters { get; set; }

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
    Relevance,      // Default: best title match first (popularity when there is no query)
    Newest,         // CreatedAt DESC
    LastUpdated,    // LastUpdatedAt DESC
    MostPopular,    // TotalViews DESC
    HighestRated,   // TotalAverageScore DESC
    MostReviewed    // ReviewCount DESC
}
