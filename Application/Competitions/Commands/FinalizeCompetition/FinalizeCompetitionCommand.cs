using Application.Competitions.DTOs;
using MediatR;

namespace Application.Competitions.Commands.FinalizeCompetition;

public class FinalizeCompetitionCommand : IRequest<List<CompetitionWinnerDto>>
{
    public Guid CompetitionId { get; set; }
}
