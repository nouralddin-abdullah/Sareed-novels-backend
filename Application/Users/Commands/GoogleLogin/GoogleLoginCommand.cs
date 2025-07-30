using Application.Users.Commands.UserLogin;
using MediatR;
namespace Application.Users.Commands.GoogleLogin;

public class GoogleLoginCommand : IRequest<UserLoginResult>
{
    public string IdToken { get; set; } = default!; 
}
