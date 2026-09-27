using Application.Services;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services;

/// <summary>Google's own validation (signature against Google's keys, expiry, issuer), with this app's client id as audience.</summary>
internal class GoogleIdTokenValidator(IConfiguration configuration) : IGoogleIdTokenValidator
{
    public Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken) =>
        GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { configuration["Google:ClientID"] }
        });
}
