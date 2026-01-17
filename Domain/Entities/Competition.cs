namespace Domain.Entities;

public class Competition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? ImageUrl { get; set; }
    
    // Prize Configuration
    public decimal TotalPrize { get; set; }
    public decimal PrizeFirstPlace { get; set; }
    public decimal PrizeSecondPlace { get; set; }
    public decimal PrizeThirdPlace { get; set; }
    
    // Schedule - Three Phases
    public DateTime ParticipationStartDate { get; set; }
    public DateTime ParticipationEndDate { get; set; }
    public DateTime JudgmentStartDate { get; set; }
    public DateTime JudgmentEndDate { get; set; }
    public DateTime ResultsDate { get; set; }
    
    // Dynamic Rules for Novel Eligibility
    public int? MaxNovelAgeDays { get; set; } // e.g., 30 = only novels created in last 30 days
    public int MinChapters { get; set; } = 5; // Minimum published chapters required
    
    // Status - Updated by admin via UpdateCompetition endpoint
    public string Status { get; set; } = CompetitionStatus.Upcoming;
    public bool IsActive { get; set; } = true;
    
    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation
    public ICollection<CompetitionParticipant> Participants { get; set; } = new List<CompetitionParticipant>();
    public ICollection<CompetitionWinner> Winners { get; set; } = new List<CompetitionWinner>();
    
    // Simple check based on stored Status (no date calculations)
    public bool CanJoin() => IsActive && Status == CompetitionStatus.Participation;
}

public static class CompetitionStatus
{
    public const string Upcoming = "Upcoming";
    public const string Participation = "Participation";
    public const string Judging = "Judging";
    public const string Completed = "Completed";
}
