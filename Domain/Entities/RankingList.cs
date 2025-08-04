namespace Domain.Entities;

public class RankingList
{
    public int Id { get; set; }
    public string Name { get; set; } = default!; // "TopRomance", "TrendingFantasy", "AllTimeGreats"
    public int? GenreId { get; set; } // null for site-wide rankings
    public Genre? Genre { get; set; }
    public string RankingType { get; set; } = default!; // "TopRated", "Trending", "NewHot", "AllTime"
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public int TotalNovels { get; set; } = 0; // How many novels are in this ranking

    // Navigation
    public ICollection<RankingEntry> Entries { get; set; } = new List<RankingEntry>();
}
