using MediatR;

namespace Application.Users.Commands.FollowUser;

public class FollowUserCommand : IRequest<OperationResult>
{
    public string UserIdToFollow { get; set; } = default!;
}

public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = default!;
}
