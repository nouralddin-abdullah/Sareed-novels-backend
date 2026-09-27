namespace Application.Services;

/// <summary>
/// Access tokens are stateless JWTs that live 60 days, so signing someone out means refusing the tokens they already
/// hold. Each user has a cut-off (User.TokensValidAfter): every authenticated request checks the token's issue time
/// against it, from a short per-instance cache. Null for everyone until something revokes, so nobody is signed out
/// by default.
/// </summary>
/// <remarks>
/// The cut-off has whole-second precision, like the tokens' "iat": a token issued in the same second as the
/// revocation still works. That is what lets a caller revoke and then issue a fresh token for the current session.
/// </remarks>
public interface ITokenRevocationService
{
    /// <summary>
    /// Signs the user out everywhere: every access token issued to them before now is refused from the next request
    /// on. For account takeover fixes, password changes and account deletion. Call it after the surrounding
    /// transaction (if any) has committed, so no other request can cache the old cut-off.
    /// </summary>
    Task RevokeAllTokensAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a token issued to <paramref name="userId"/> at <paramref name="issuedAtUtc"/> is still accepted: false
    /// when it was issued before the user's cut-off, or when the user no longer exists.
    /// </summary>
    Task<bool> IsTokenActiveAsync(string userId, DateTime issuedAtUtc, CancellationToken cancellationToken = default);
}
