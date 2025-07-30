using MediatR;

namespace Application.Users.Commands.UserLogin;

public record UserLoginResult(string AccessToken, DateTime ExpiresFor);

public class UserLoginCommand : IRequest<UserLoginResult>
{
    public string LoginCardinality { get; set; } = default!;
    public string Password { get; set; } = default!;

}
