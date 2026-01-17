using MediatR;

namespace Application.Competitions.Commands.LeaveCompetition;

public class LeaveCompetitionCommand : IRequest<bool>
{
    public Guid CompetitionId { get; set; }
    public Guid NovelId { get; set; }
}
