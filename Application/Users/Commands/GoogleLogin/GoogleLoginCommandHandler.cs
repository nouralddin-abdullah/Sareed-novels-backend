using Application.Services;
using Application.Users.Commands.UserLogin;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Moderation;
using Domain.Repositories;
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
        IUsersRepository users,
        GoogleUserNames googleUserNames,
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
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await googleTokens.ValidateAsync(request.IdToken);
            }
            catch (InvalidJwtException ex)
            {
                logger.LogWarning(ex, "Invalid Google ID token");
                throw new ForbidException(InvalidTokenMessage, InvalidTokenCode);
            }

            // Accounts are matched by email, so an unverified Google email must not sign in to (or be linked
            // with) the Sard account that uses that address.
            if (!payload.EmailVerified)
            {
                logger.LogWarning("Google sign-in refused: email not verified by Google");
                throw new ForbidException(EmailNotVerifiedMessage, EmailNotVerifiedCode);
            }

            logger.LogInformation("Google authentication successful");

            // Find or create user
            var user = await userManager.FindByEmailAsync(payload.Email);
            var passwordReset = false;
            var isNewAccount = user == null;

            // A moderator suspended the account: no sign-in, and nothing about it changes.
            if (user != null && Suspension.IsActive(user.SuspendedUntil, time.GetUtcNow().UtcDateTime))
            {
                logger.LogInformation("Google sign-in refused: user {UserId} is suspended", user.Id);
                throw new AccountSuspendedException(user.SuspendedUntil!.Value);
            }

            if (user == null)
            {
                user = await CreateUserAsync(payload, cancellationToken);

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

            return new UserLoginResult(accessToken, expiresAt, passwordReset, isNewAccount);
        }

        /// <summary>
        /// A new account for a Google user. The handle is public (/profile/{userName}), so it never comes from the email
        /// address, and neither does the display name: the handle is one from the person's Google name when it is Latin,
        /// else "sarduser" and six digits (<see cref="GoogleUserNames"/>, #69). A handle another account holds when this
        /// one is saved, also one a sign-in at the same moment just took, gives way to the next.
        /// </summary>
        private async Task<User> CreateUserAsync(GoogleJsonWebSignature.Payload payload, CancellationToken cancellationToken)
        {
            IdentityResult? result = null;
            var attempts = 0;
            foreach (var userName in await googleUserNames.CandidatesAsync(payload, cancellationToken))
            {
                attempts++;
                var user = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = userName,
                    Email = payload.Email,
                    DisplayName = string.IsNullOrWhiteSpace(payload.Name) ? userName : payload.Name,
                    EmailConfirmed = payload.EmailVerified,
                    ProfilePhoto = payload.Picture,
                    CreatedAt = DateTime.UtcNow
                };

                result = await users.CreateWithoutPasswordAsync(user);
                if (result.Succeeded)
                {
                    return user;
                }
                if (!result.Errors.All(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
                {
                    break;
                }
            }

            // Every handle was in use or taken when tried, or the account was refused for another reason (the address
            // was just taken by a sign-in running at the same time): an answer, not a server error.
            logger.LogError("Failed to create Google user after {Attempts} attempt(s): {Errors}",
                attempts, result is null ? "every user name to try is in use" : string.Join(", ", result.Errors.Select(e => e.Code)));
            throw new BadRequestException(SignInFailedMessage, SignInFailedCode);
        }

        public const string InvalidTokenCode = "GoogleTokenInvalid";
        public const string InvalidTokenMessage = "تعذّر التحقق من حساب Google. حاول تسجيل الدخول مرة أخرى.";
        public const string EmailNotVerifiedCode = "GoogleEmailNotVerified";
        public const string EmailNotVerifiedMessage = "لم تؤكّد Google البريد الإلكتروني لهذا الحساب، فلا يمكن تسجيل الدخول به. أكّد بريدك لدى Google ثم حاول مرة أخرى.";
        public const string SignInFailedCode = "GoogleSignInFailed";
        public const string SignInFailedMessage = "تعذّر إنشاء حسابك بحساب Google الآن. حاول مرة أخرى.";

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
