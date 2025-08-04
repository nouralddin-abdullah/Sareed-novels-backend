namespace Domain.Entities;

public class RankingEntry
{
    public int Id { get; set; }
    public int RankingListId { get; set; }
    public RankingList RankingList { get; set; } = default!;

    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;

    public int Rank { get; set; } // 1, 2, 3, etc.
    public decimal Score { get; set; } // The calculated score that determined this rank
    public decimal QualityScore { get; set; } // For debugging/display
    public decimal PopularityScore { get; set; } // For debugging/display
    public decimal TrendingScore { get; set; } // For debugging/display

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
