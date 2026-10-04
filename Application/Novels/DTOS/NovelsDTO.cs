using Domain.Entities;

namespace Application.Novels.DTOS;

public class NovelsDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public List<GenreSmallDto> GenresList { get; set; } = new List<GenreSmallDto>();
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
    public int ChapterCount { get; set; }
    public AuthorDTO Author { get; set; } = default!;
}

/// <summary>
/// A member shown as an author: the novel page, search results and a reading list's novels (#59), and the ranked lists
/// (#68), give a novel's author this way, read from the account as it is now, so a rename shows at once.
/// </summary>
public class AuthorDTO
{
    public string Id { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    /// <summary>The photo's URL; null when the member has none.</summary>
    public string? ProfilePhoto { get; set; }
}

public class GenreSmallDto
{
    public int Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
}

/// <summary>
/// A novel in a ranked list: the rankings (GET /api/rankings/{genreSlug}/{type} and /site-wide/{type}) and a genre's
/// novels (GET /api/genre/{genreSlug}/novels), in every sorting.
/// </summary>
public class NovelInRankingDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public List<GenreSmallDto> GenresList { get; set; } = new List<GenreSmallDto>();
    public string Slug { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public string Status { get; set; } = default!;
    public int TotalViews { get; set; }
    public decimal TotalAverageScore { get; set; }
    public int ReviewCount { get; set; }
    /// <summary>
    /// Who wrote it (#68): the novel page's own author, as search results and reading lists give it (#59), read with
    /// the page. A ranking stores only its novels' places, so a rename shows in the next answer.
    /// </summary>
    public AuthorDTO Author { get; set; } = default!;
}