using Application.Users.Commands.UserLogin;
using MediatR;

namespace Application.Users.Commands.GoogleCallback;

public class GoogleCallbackCommand : IRequest<UserLoginResult>
{
    public string Code { get; set; } = default!;
    public string? State { get; set; }
}