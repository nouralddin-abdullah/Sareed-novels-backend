using Application.Competitions.DTOs;
using MediatR;

namespace Application.Competitions.Queries.GetCompetitionLeaderboard;

public class GetCompetitionLeaderboardQuery : IRequest<List<CompetitionLeaderboardEntryDto>>
{
    public Guid CompetitionId { get; set; }
    public int Top { get; set; } = 10;
}
