using Application.Competitions.DTOs;
using MediatR;

namespace Application.Competitions.Commands.JoinCompetition;

public class JoinCompetitionCommand : IRequest<CompetitionParticipantDto>
{
    public Guid CompetitionId { get; set; }
    public Guid NovelId { get; set; }
}
