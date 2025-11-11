using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Entities.Commands.DeleteRelationship;

public class DeleteRelationshipCommand(Guid relationshipId) : IRequest<OperationResult>
{
    public Guid RelationshipId { get; set; } = relationshipId;
}
