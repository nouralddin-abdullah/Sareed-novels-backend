namespace Domain.Entities;

public class NovelGenre
{
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;

    public int GenreId { get; set; }
    public Genre Genre { get; set; } = default!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public decimal? GenreScore { get; set; }
    public int? GenreRank { get; set; }
    public DateTime? LastRankUpdate { get; set; }

    public decimal QualityScore { get; set; } = 0; // Bayesian average of ratings
    public decimal PopularityScore { get; set; } = 0; // Time-weighted views
    public decimal TrendingScore { get; set; } = 0; // Recent activity score
    public int ViewsLast7Days { get; set; } = 0; // For trending calculation
    public int ViewsLast30Days { get; set; } = 0; // For popularity calculation
    public int ReviewsLast30Days { get; set; } = 0; // For trending calculation
}
