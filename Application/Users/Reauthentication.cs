using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users;

/// <summary>
/// The signed-in member proves it's them again before a change to their account that a stolen session shouldn't be
/// able to make: deleting it (DELETE /api/User/me) and giving it its first password (POST /api/User/set-password).
/// What counts:
/// <list type="bullet">
/// <item>the account's password, for an action that takes it (deletion) and an account that has one;</item>
/// <item>a Google ID token, from signing in with Google just now, whose subject is one of the account's Google
/// sign-ins;</item>
/// <item>nothing, for an account without a password whose access token comes from a sign-in in the last
/// <see cref="RecentSignIn"/> (the web signs in with Google by redirect and never holds an ID token, so it has the
/// member sign in again instead).</item>
/// </list>
/// A refusal is a <see cref="ForbidException"/> (403): <see cref="FailedCode"/> for a proof that doesn't hold,
/// <see cref="RequiredCode"/> when nothing that counts was sent. The messages that name the action are the caller's
/// (<see cref="ReauthenticationMessages"/>); those about the password or the Google account are the same for every
/// action.
/// </summary>
public sealed class Reauthentication(
    ILogger<Reauthentication> logger,
    UserManager<User> userManager,
    IGoogleIdTokenValidator googleTokens,
    TimeProvider time)
{
    /// <summary>
    /// How recent the sign-in behind the access token must be for an account without a password to confirm with nothing
    /// else.
    /// </summary>
    public static readonly TimeSpan RecentSignIn = TimeSpan.FromMinutes(10);

    public const string FailedCode = "ReauthenticationFailed";
    public const string RequiredCode = "ReauthenticationRequired";

    public const string WrongPasswordMessage = "كلمة المرور غير صحيحة";
    public const string InvalidGoogleTokenMessage = "تعذّر التحقق من حساب Google، سجّل الدخول به مرة أخرى";
    public const string GoogleAccountNotLinkedMessage = "حساب Google هذا غير مرتبط بحسابك في سرد";

    private const string GoogleProvider = "Google";

    /// <summary>
    /// For an action that also takes the account's password (deletion): the <paramref name="password"/> first when one
    /// is sent, then the Google ID token, then a recent sign-in.
    /// </summary>
    public Task ConfirmAsync(
        User user, CurrentUser currentUser, string? password, string? googleIdToken,
        ReauthenticationMessages messages, PasswordReauthenticationMessages passwordMessages) =>
        CheckAsync(user, currentUser, password, googleIdToken, messages, passwordMessages);

    /// <summary>
    /// For an action whose proof is Google's alone (setting a first password, when the account has none to check): the
    /// Google ID token, else a recent sign-in.
    /// </summary>
    public Task ConfirmAsync(
        User user, CurrentUser currentUser, string? googleIdToken, ReauthenticationMessages messages) =>
        CheckAsync(user, currentUser, password: null, googleIdToken, messages, passwordMessages: null);

    private async Task CheckAsync(
        User user, CurrentUser currentUser, string? password, string? googleIdToken,
        ReauthenticationMessages messages, PasswordReauthenticationMessages? passwordMessages)
    {
        var hasPassword = user.PasswordHash != null;

        if (passwordMessages is not null && !string.IsNullOrEmpty(password))
        {
            if (!hasPassword)
            {
                throw Refused(user, messages, "no password on the account", passwordMessages.AccountHasNone);
            }
            if (!await userManager.CheckPasswordAsync(user, password))
            {
                throw Refused(user, messages, "wrong password", WrongPasswordMessage);
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(googleIdToken))
        {
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await googleTokens.ValidateAsync(googleIdToken);
            }
            catch (InvalidJwtException ex)
            {
                logger.LogWarning(ex, "{Action} by {UserId}: invalid Google ID token", messages.Action, user.Id);
                throw Refused(user, messages, "invalid Google ID token", InvalidGoogleTokenMessage);
            }

            var logins = await userManager.GetLoginsAsync(user);
            if (!logins.Any(l => l.LoginProvider == GoogleProvider && l.ProviderKey == payload.Subject))
            {
                throw Refused(user, messages, "Google account not linked", GoogleAccountNotLinkedMessage);
            }
            return;
        }

        // Nothing sent: an account that signs in with Google only may rely on the sign-in it just made.
        var now = time.GetUtcNow().UtcDateTime;
        if (!hasPassword && currentUser.TokenIssuedAt is { } issuedAt && now - issuedAt <= RecentSignIn)
        {
            return;
        }

        logger.LogInformation("{Action} by {UserId} refused: re-authentication required", messages.Action, user.Id);
        throw new ForbidException(
            hasPassword && passwordMessages is not null ? passwordMessages.Required : messages.SignInAgain,
            RequiredCode);
    }

    private ForbidException Refused(User user, ReauthenticationMessages messages, string why, string message)
    {
        logger.LogWarning("{Action} by {UserId} refused: {Reason}", messages.Action, user.Id, why);
        return new ForbidException(message, FailedCode);
    }
}

/// <summary>An action's messages for the refusals that name it.</summary>
/// <param name="Action">What is being confirmed, for the logs: "Account deletion", "Setting a password".</param>
/// <param name="SignInAgain">
/// <see cref="Reauthentication.RequiredCode"/> for an account without a password: nothing was sent, or the sign-in
/// behind the access token is older than <see cref="Reauthentication.RecentSignIn"/>. It asks for a new Google sign-in.
/// </param>
public sealed record ReauthenticationMessages(string Action, string SignInAgain);

/// <summary>The messages of an action that also takes the account's password as proof.</summary>
/// <param name="Required">
/// <see cref="Reauthentication.RequiredCode"/> for an account with a password that sent nothing.
/// </param>
/// <param name="AccountHasNone">
/// <see cref="Reauthentication.FailedCode"/> for a password sent for an account without one.
/// </param>
public sealed record PasswordReauthenticationMessages(string Required, string AccountHasNone);
