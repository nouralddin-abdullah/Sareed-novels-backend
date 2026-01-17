namespace Domain.Entities;

public class CompetitionParticipant
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Competition Competition { get; set; } = default!;
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;
    
    // Tracking Views from Join Date
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public int ViewsAtJoin { get; set; } // Snapshot of novel's total views when joined
    
    // Points & Ranking
    public decimal CurrentPoints { get; set; } = 0;
    public decimal ExtraPoints { get; set; } = 0; // Admin-awarded extra points
    public int CurrentRank { get; set; } = 0;
    
    // Computed property for competition views (views gained during competition)
    public int CompetitionViews => (Novel?.TotalViews ?? ViewsAtJoin) - ViewsAtJoin;
}
