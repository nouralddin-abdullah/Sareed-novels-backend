using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Users.Commands.ForgotPassword;

public class ForgotPasswordCommand : IRequest<OperationResult>
{
    public string Email { get; set; } = default!;
}
