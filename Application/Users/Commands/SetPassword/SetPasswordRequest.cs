namespace Application.Users.Commands.SetPassword;

/// <summary>
/// The body of POST /api/User/set-password. Both fields are nullable, so ASP.NET doesn't refuse a missing password
/// itself (as ValidationFailed) before the handler's checks come in their order: an account that has a password is told
/// so first. The rules are <see cref="SetPasswordCommandValidator"/>'s.
/// </summary>
public class SetPasswordRequest
{
    public string? NewPassword { get; set; }

    /// <summary>An ID token from signing in with Google just now; not needed within 10 minutes of a sign-in.</summary>
    public string? GoogleIdToken { get; set; }
}
