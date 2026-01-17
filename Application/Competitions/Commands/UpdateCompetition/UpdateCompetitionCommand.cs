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
    
    // Status
    public string? Status { get; set; }
    public bool? IsActive { get; set; }
}
