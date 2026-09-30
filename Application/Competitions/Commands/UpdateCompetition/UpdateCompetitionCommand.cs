using Application.Competitions.DTOs;
using MediatR;

namespace Application.Competitions.Commands.UpdateCompetition;

public class UpdateCompetitionCommand : IRequest<CompetitionDetailDto>
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? ImageUrl { get; set; }
    
    // Prizes
    public decimal? TotalPrize { get; set; }
    public decimal? PrizeFirstPlace { get; set; }
    public decimal? PrizeSecondPlace { get; set; }
    public decimal? PrizeThirdPlace { get; set; }
    
    // Schedule
    public DateTime? ParticipationStartDate { get; set; }
    public DateTime? ParticipationEndDate { get; set; }
    public DateTime? JudgmentStartDate { get; set; }
    public DateTime? JudgmentEndDate { get; set; }
    public DateTime? ResultsDate { get; set; }
    
    // Rules
    public int? MaxNovelAgeDays { get; set; }
    public int? MinChapters { get; set; }
    
    /// <summary>
    /// Stored as the admin's override (one of the four, any letter case; 400 InvalidStatus otherwise). It counts only
    /// where it is further along than the dates: it can open early, close early or complete early, not hold a
    /// competition back (move the dates for that). Upcoming removes the override.
    /// </summary>
    public string? Status { get; set; }
    public bool? IsActive { get; set; }
}
