using Domain.Competitions;

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
    
    // Schedule (UTC). The status follows it: see CompetitionSchedule.
    public DateTime ParticipationStartDate { get; set; }
    public DateTime ParticipationEndDate { get; set; }
    public DateTime JudgmentStartDate { get; set; }
    public DateTime JudgmentEndDate { get; set; }
    public DateTime ResultsDate { get; set; }
    
    // Dynamic Rules for Novel Eligibility
    public int? MaxNovelAgeDays { get; set; } // e.g., 30 = only novels created in last 30 days
    public int MinChapters { get; set; } = 5; // Minimum published chapters required
    
    /// <summary>
    /// The stored status: what an admin set (UpdateCompetition) or finalizing did (Completed). It is an override that
    /// counts only where it is further along than the dates (<see cref="CompetitionSchedule"/>); a new competition has
    /// Upcoming, which overrides nothing. Show and act on <see cref="EffectiveStatus"/>, never this alone.
    /// </summary>
    public string Status { get; set; } = CompetitionStatus.Upcoming;
    public bool IsActive { get; set; } = true;
    
    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation
    public ICollection<CompetitionParticipant> Participants { get; set; } = new List<CompetitionParticipant>();
    public ICollection<CompetitionWinner> Winners { get; set; } = new List<CompetitionWinner>();
    
    /// <summary>The status at <paramref name="utcNow"/>: the dates, or the stored status where it is further along.</summary>
    public string EffectiveStatus(DateTime utcNow) => CompetitionSchedule.StatusAt(this, utcNow);

    /// <summary>Whether a novel can join at <paramref name="utcNow"/>: an active competition in Participation.</summary>
    public bool CanJoin(DateTime utcNow) => IsActive && EffectiveStatus(utcNow) == CompetitionStatus.Participation;

    /// <summary>
    /// Whether a novel can be withdrawn at <paramref name="utcNow"/>: until participation ends (Upcoming, for a
    /// competition postponed after novels joined, or Participation).
    /// </summary>
    public bool CanLeave(DateTime utcNow) =>
        EffectiveStatus(utcNow) is CompetitionStatus.Upcoming or CompetitionStatus.Participation;
}

public static class CompetitionStatus
{
    public const string Upcoming = "Upcoming";
    public const string Participation = "Participation";
    public const string Judging = "Judging";
    public const string Completed = "Completed";
}
