using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Entities.Commands.UpdateRelationship;

public class UpdateRelationshipCommand : IRequest<OperationResult>
{
    public Guid RelationshipId { get; set; }
    public string? RelationType { get; set; }
    public string? Label { get; set; }
    public string? ReverseLabel { get; set; }
    public string? Description { get; set; }
}
