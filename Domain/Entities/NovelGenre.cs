namespace Domain.Entities;

public class NovelGenre
{
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;

    public int GenreId { get; set; }
    public Genre Genre { get; set; } = default!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public decimal? GenreScore { get; set; } // Novel's score within this genre
    public int? GenreRank { get; set; } // Novel's rank within this genre (1-100)
    public DateTime? LastRankUpdate { get; set; }
}
