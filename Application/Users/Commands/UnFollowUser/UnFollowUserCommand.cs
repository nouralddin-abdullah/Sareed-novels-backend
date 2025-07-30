using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Users.Commands.UnFollowUser;

public class UnFollowUserCommand : IRequest<OperationResult>
{
    public string UserToUnFollowId { get; set; } = default!;
}
