using Application.Services;
using Application.Users.Commands.UserLogin;
using Domain.Entities;
using Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;

namespace Application.Users.Commands.GoogleLogin
{
    public class GoogleLoginCommandHandler(
        ILogger<GoogleLoginCommandHandler> logger,
        UserManager<User> userManager,
        IJWTService jwtService, 
        IConfiguration configuration,
        ISearchIndexQueueService searchIndexQueue) : IRequestHandler<GoogleLoginCommand, UserLoginResult>
    {

        public async Task<UserLoginResult> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var googleClientId = configuration["Google:ClientID"];

                // Verify Google ID token
                var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { googleClientId }
                });

                logger.LogInformation("Google authentication successful for email: {email}", payload.Email);

                // Find or create user
                var user = await userManager.FindByEmailAsync(payload.Email);
                bool isNewUser = false;

                if (user == null)
                {
                    // Create new user from Google account
                    user = new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserName = payload.Email,
                        Email = payload.Email,
                        DisplayName = payload.Name ?? payload.Email,
                        EmailConfirmed = payload.EmailVerified,
                        ProfilePhoto = payload.Picture,
                        CreatedAt = DateTime.UtcNow
                    };

                    var createResult = await userManager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                        logger.LogError("Failed to create Google user: {errors}", errors);
                        throw new InvalidOperationException($"Failed to create user: {errors}");
                    }

                    // Add Google login
                    var addLoginResult = await userManager.AddLoginAsync(user, new UserLoginInfo("Google", payload.Subject, "Google"));
                    if (!addLoginResult.Succeeded)
                    {
                        logger.LogWarning("Failed to add Google login for user {userId}", user.Id);
                    }

                    isNewUser = true;
                    logger.LogInformation("Created new user from Google account: {userId}", user.Id);
                }
                else
                {
                    // Check if Google login already exists
                    var existingLogin = await userManager.FindByLoginAsync("Google", payload.Subject);
                    if (existingLogin == null)
                    {
                        // Add Google login to existing account
                        var addLoginResult = await userManager.AddLoginAsync(user, new UserLoginInfo("Google", payload.Subject, "Google"));
                        if (!addLoginResult.Succeeded)
                        {
                            logger.LogWarning("Failed to add Google login for existing user {userId}", user.Id);
                        }
                    }

                    logger.LogInformation("Google login successful for existing user: {userId}", user.Id);
                }

                // Queue new user for Elasticsearch indexing
                if (isNewUser)
                {
                    await searchIndexQueue.QueueUserIndexAsync(user.Id);
                    logger.LogDebug("Queued new Google user {UserId} for search indexing", user.Id);
                }

                // Generate JWT token
                var accessToken = jwtService.GenerateAccessToken(user);
                var expiresAt = DateTime.UtcNow.AddDays(60);

                return new UserLoginResult(accessToken, expiresAt);
            }
            catch (InvalidJwtException ex)
            {
                logger.LogError(ex, "Invalid Google ID token");
                throw new ForbidException("Invalid Google token");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Google authentication failed");
                throw new InvalidOperationException("Google authentication failed");
            }
        }
    }
}

