using System.Text.Json;
using Application.Users.Commands.GoogleLogin;
using Application.Users.Commands.UserLogin;
using Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.GoogleCallback
{
    public class GoogleCallbackCommandHandler(
        ILogger<GoogleCallbackCommandHandler> logger,
        IMediator mediator, IConfiguration configuration) : IRequestHandler<GoogleCallbackCommand, UserLoginResult>
    {

        public async Task<UserLoginResult> Handle(GoogleCallbackCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var googleClientId = configuration["Google:ClientID"];
                var googleClientSecret = configuration["Google:ClientSecret"];
                var redirectUri = configuration["Google:RedirectUri"];

                logger.LogInformation("Exchanging authorization code with redirect_uri: {redirectUri}", redirectUri);

                // Exchange authorization code for tokens
                using var httpClient = new HttpClient();
                var tokenRequest = new Dictionary<string, string>
                {
                    ["client_id"] = googleClientId!,
                    ["client_secret"] = googleClientSecret!,
                    ["code"] = request.Code,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = redirectUri!
                };

                logger.LogInformation("Token request parameters: {parameters}",
                    string.Join(", ", tokenRequest.Select(kvp => $"{kvp.Key}={kvp.Value.Substring(0, Math.Min(kvp.Value.Length, 20))}...")));

                var tokenResponse = await httpClient.PostAsync("https://oauth2.googleapis.com/token",
                    new FormUrlEncodedContent(tokenRequest), cancellationToken);

                if (!tokenResponse.IsSuccessStatusCode)
                {
                    var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogError("Failed to exchange authorization code: {error}", errorContent);
                    throw new ForbidException("Failed to exchange authorization code");
                }

                var tokenContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenContent);

                var idToken = tokenData.GetProperty("id_token").GetString();
                if (string.IsNullOrEmpty(idToken))
                {
                    throw new ForbidException("No ID token received from Google");
                }

                logger.LogInformation("Successfully received ID token from Google");

                // Reuse your existing GoogleLoginCommand with the received ID token
                var googleLoginCommand = new GoogleLoginCommand { IdToken = idToken };
                return await mediator.Send(googleLoginCommand, cancellationToken);
            }
            catch (Exception ex) when (ex is not ForbidException)
            {
                logger.LogError(ex, "Google callback failed for code: {code}", request.Code);
                throw new InvalidOperationException("Google authentication failed");
            }
        }
    }
}