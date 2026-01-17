using MediatR;

namespace Application.Competitions.Commands.AddExtraPoints;

public class AddExtraPointsCommand : IRequest<bool>
{
    public Guid CompetitionId { get; set; }
    public Guid NovelId { get; set; }
    public decimal ExtraPoints { get; set; }
}
