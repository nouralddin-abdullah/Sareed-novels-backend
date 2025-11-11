using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Entities.Commands.DeleteEntity;

public class DeleteEntityCommand(Guid entityId) : IRequest<OperationResult>
{
    public Guid EntityId { get; set; } = entityId;
}
