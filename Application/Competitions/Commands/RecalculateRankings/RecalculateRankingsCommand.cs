using Application.Competitions.DTOs;
using MediatR;

namespace Application.Competitions.Commands.RecalculateRankings;

public class RecalculateRankingsCommand : IRequest<List<CompetitionLeaderboardEntryDto>>
{
    public Guid CompetitionId { get; set; }
}
