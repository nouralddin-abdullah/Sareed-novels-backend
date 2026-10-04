using MediatR;

namespace Application.Users.Commands.UserLogin;

/// <param name="PasswordReset">
/// True when this Google sign-in took over an account whose email address had never been verified: the password
/// someone had set on it was removed and every earlier session ended. Clients tell the person so (they can sign in
/// with Google, or set a new password with "forgot password").
/// </param>
/// <param name="IsNewAccount">
/// True only on the Google sign-in that created the account (#69): the app then lets the member choose their user name
/// once. False on every other sign-in, email and password included.
/// </param>
public record UserLoginResult(string AccessToken, DateTime ExpiresFor, bool PasswordReset = false, bool IsNewAccount = false);

public class UserLoginCommand : IRequest<UserLoginResult>
{
    public string LoginCardinality { get; set; } = default!;
    public string Password { get; set; } = default!;

}
