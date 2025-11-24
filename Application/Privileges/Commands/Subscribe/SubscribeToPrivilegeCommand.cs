using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Privileges.Commands.Subscribe;

public class SubscribeToPrivilegeCommand : IRequest<OperationResult>
{
    public Guid NovelId { get; set; }
}
