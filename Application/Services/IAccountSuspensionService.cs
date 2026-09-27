namespace Application.Services;

/// <summary>
/// Moderators suspending accounts (User.SuspendedUntil). A suspended account can't sign in (password or Google), and
/// its access tokens are refused on every request (<see cref="ITokenRevocationService.IsTokenActiveAsync"/>).
/// </summary>
public interface IAccountSuspensionService
{
    /// <summary>
    /// Suspends the user until <paramref name="until"/> (UTC; Suspension.Permanent for good) and ends every session
    /// they have (<see cref="ITokenRevocationService.RevokeAllTokensAsync"/>, which also unregisters their push
    /// devices). A suspension only gets longer this way: lift it first to shorten it. Returns when the suspension
    /// ends, or null when there's no such user.
    /// </summary>
    Task<DateTime?> SuspendAsync(string userId, DateTime until, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the user's suspension: they can sign in again (the sessions the suspension ended stay ended). False when
    /// there's no such user.
    /// </summary>
    Task<bool> LiftAsync(string userId, CancellationToken cancellationToken = default);
}
