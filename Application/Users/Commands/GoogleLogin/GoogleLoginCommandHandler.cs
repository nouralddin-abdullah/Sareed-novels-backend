using Application.Services;
using Application.Users.Commands.UserLogin;
using Domain.Entities;
using Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Google.Apis.Auth;

namespace Application.Users.Commands.GoogleLogin
{
    public class GoogleLoginCommandHandler(
        ILogger<GoogleLoginCommandHandler> logger,
        UserManager<User> userManager,
        IJWTService jwtService,
        IGoogleIdTokenValidator googleTokens,
        ITokenRevocationService tokenRevocation,
        IServiceScopeFactory scopeFactory,
        TimeProvider time) : IRequestHandler<GoogleLoginCommand, UserLoginResult>
    {
        private const string GoogleProvider = "Google";

        /// <summary>The email telling someone that a Google sign-in removed the password from their account.</summary>
        public const string PasswordRemovedEmailTemplate = "password-removed";

        public async Task<UserLoginResult> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var payload = await googleTokens.ValidateAsync(request.IdToken);

                // Accounts are matched by email, so an unverified Google email must not sign in to (or be linked
                // with) the Sard account that uses that address.
                if (!payload.EmailVerified)
                {
                    logger.LogWarning("Google sign-in refused: email not verified by Google");
                    throw new ForbidException("Your Google account's email address is not verified");
                }

                logger.LogInformation("Google authentication successful");

                // Find or create user
                var user = await userManager.FindByEmailAsync(payload.Email);
                var passwordReset = false;

                if (user == null)
                {
                    // Create new user from Google account. The handle is public (/profile/{userName}), so it is never
                    // derived from the email address, and neither is the display name.
                    var randomNumber = new Random().Next(100000, 999999);
                    var userName = $"sarduser{randomNumber}";
                    user = new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserName = userName,
                        Email = payload.Email,
                        DisplayName = string.IsNullOrWhiteSpace(payload.Name) ? userName : payload.Name,
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
                    var addLoginResult = await userManager.AddLoginAsync(user, new UserLoginInfo(GoogleProvider, payload.Subject, GoogleProvider));
                    if (!addLoginResult.Succeeded)
                    {
                        logger.LogWarning("Failed to add Google login for user {userId}", user.Id);
                    }

                    logger.LogInformation("Created new user from Google account: {userId}", user.Id);
                }
                else
                {
                    if (!user.EmailConfirmed)
                    {
                        passwordReset = await HandOverToEmailOwnerAsync(user, payload.Subject, cancellationToken);
                    }

                    // Check if Google login already exists
                    var existingLogin = await userManager.FindByLoginAsync(GoogleProvider, payload.Subject);
                    if (existingLogin == null)
                    {
                        // Add Google login to existing account
                        var addLoginResult = await userManager.AddLoginAsync(user, new UserLoginInfo(GoogleProvider, payload.Subject, GoogleProvider));
                        if (!addLoginResult.Succeeded)
                        {
                            logger.LogWarning("Failed to add Google login for existing user {userId}", user.Id);
                        }
                    }

                    logger.LogInformation("Google login successful for existing user: {userId}", user.Id);
                }


                // Generate JWT token
                var accessToken = jwtService.GenerateAccessToken(user);
                var expiresAt = DateTime.UtcNow.AddDays(60);

                return new UserLoginResult(accessToken, expiresAt, passwordReset);
            }
            catch (InvalidJwtException ex)
            {
                logger.LogError(ex, "Invalid Google ID token");
                throw new ForbidException("Invalid Google token");
            }
            catch (ForbidException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Google authentication failed");
                throw new InvalidOperationException("Google authentication failed");
            }
        }

        /// <summary>
        /// The account's email address was never verified: sign-up doesn't check it, so whoever registered it may not
        /// own it (an account pre-registered with someone else's address, waiting for its owner to sign in with
        /// Google). Google has just verified that this person owns the address, so the account is handed to them: the
        /// address counts as confirmed, the password set before is removed, lockout is cleared, other external
        /// sign-ins are unlinked and every existing session ends.
        /// </summary>
        /// <returns>Whether a password was removed.</returns>
        private async Task<bool> HandOverToEmailOwnerAsync(User user, string googleSubject, CancellationToken cancellationToken)
        {
            foreach (var login in await userManager.GetLoginsAsync(user))
            {
                if (login.LoginProvider == GoogleProvider && login.ProviderKey == googleSubject)
                {
                    continue;
                }
                EnsureSucceeded(await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey), user);
            }

            var hadPassword = user.PasswordHash != null;
            user.EmailConfirmed = true;
            user.PasswordHash = null;
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            user.TokensValidAfter = User.TokenCutoff(time.GetUtcNow().UtcDateTime);

            // One save for all of the above, with a new security stamp, so the account is never left half handed
            // over (the password gone but the old sessions still valid, or the other way round).
            EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user), user);

            // Applies the cut-off on this instance at once, rather than when its cached copy expires.
            await tokenRevocation.RevokeAllTokensAsync(user.Id, cancellationToken);

            logger.LogWarning(
                "Google sign-in took over account {UserId}, whose email address was never verified: password removed: {PasswordRemoved}, earlier sessions revoked",
                user.Id, hadPassword);

            if (hadPassword)
            {
                EmailPasswordRemovedNotice(user.Email!, user.Id);
            }
            return hadPassword;
        }

        private static void EnsureSucceeded(IdentityResult result, User user)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not hand account {user.Id} over to its email owner: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }

        /// <summary>
        /// Best effort and off the request (SMTP can take half a minute to time out): the sign-in response already
        /// carries the passwordReset flag for the app and the web to show.
        /// </summary>
        private void EmailPasswordRemovedNotice(string email, string userId)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                    await emailSender.SendTemplateEmailAsync(email, PasswordRemovedEmailTemplate, new { });
                    logger.LogInformation("Emailed the password-removed notice to user {UserId}", userId);
                }
                catch (Exception ex)
                {
                    // Also where an unconfigured SMTP ends up (the email sender can't be created or can't connect).
                    logger.LogError(ex, "Could not email the password-removed notice to user {UserId}", userId);
                }
            }, CancellationToken.None);
        }
    }
}
