using Google.Apis.Auth;

namespace Application.Services;

/// <summary>Checks a Google ID token (signature, expiry, audience = this app's client id) and returns its claims.</summary>
public interface IGoogleIdTokenValidator
{
    /// <exception cref="InvalidJwtException">The token is not a valid Google ID token for this app.</exception>
    Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken);
}
