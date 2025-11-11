using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Entities.Commands.CreateRelationship;

public class CreateRelationshipCommand : IRequest<OperationResult>
{
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public string RelationType { get; set; } = default!;
    public string? Label { get; set; }
    public string? ReverseLabel { get; set; }
    public string? Description { get; set; }
}
