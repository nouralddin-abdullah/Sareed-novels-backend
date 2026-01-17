namespace Domain.Entities;

public class CompetitionWinner
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Competition Competition { get; set; } = default!;
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;
    public string AuthorId { get; set; } = default!;
    public User Author { get; set; } = default!;
    
    // Final Results (captured at competition end)
    public int Rank { get; set; } // 1, 2, 3
    public decimal FinalPoints { get; set; }
    public int FinalViews { get; set; } // Competition views at end
    public decimal PrizeWon { get; set; }
    
    public DateTime AwardedAt { get; set; } = DateTime.UtcNow;
}
