using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Google.Apis.Auth;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.DeleteAccount;

/// <summary>
/// DELETE /api/User/me: the signed-in member deletes their account for good (<see cref="IAccountDeletionService"/>).
/// They prove it's them again first, with one of:
/// <list type="bullet">
/// <item><see cref="Password"/>, for an account with a password;</item>
/// <item><see cref="GoogleIdToken"/>, from signing in with Google just now, for an account with a Google sign-in (the
/// token's subject must be that sign-in);</item>
/// <item>nothing, for an account without a password whose access token comes from a sign-in in the last
/// <see cref="DeleteAccountCommandHandler.RecentSignIn"/> (the web signs in with Google by redirect and never holds an
/// ID token, so it has the member sign in again instead).</item>
/// </list>
/// </summary>
public class DeleteAccountCommand : IRequest
{
    public string? Password { get; set; }
    public string? GoogleIdToken { get; set; }
}

public class DeleteAccountCommandHandler(
    ILogger<DeleteAccountCommandHandler> logger,
    IUserContext userContext,
    UserManager<User> userManager,
    IGoogleIdTokenValidator googleTokens,
    IAccountDeletionService accountDeletion,
    AccountDeletionAttempts attempts,
    TimeProvider time) : IRequestHandler<DeleteAccountCommand>
{
    /// <summary>How recent the sign-in behind the access token must be for an account without a password to confirm with nothing else.</summary>
    public static readonly TimeSpan RecentSignIn = TimeSpan.FromMinutes(10);

    public const string ReauthenticationFailed = "ReauthenticationFailed";
    public const string ReauthenticationRequired = "ReauthenticationRequired";
    public const string AdminCannotDeleteAccount = "AdminCannotDeleteAccount";
    public const string TooManyAttempts = "TooManyDeletionAttempts";

    private const string GoogleProvider = "Google";

    public async Task Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

        // Every attempt counts, before the password is checked: this is what stops password guessing here.
        if (!attempts.TryAcquire(currentUser.Id))
        {
            logger.LogWarning("User {UserId} reached the account deletion attempt limit", currentUser.Id);
            throw new TooManyRequestsException("حاولت حذف حسابك مرات كثيرة، حاول مرة أخرى بعد ساعة", TooManyAttempts);
        }

        var user = await userManager.FindByIdAsync(currentUser.Id);
        if (user is null || user.DeletedAt != null)
        {
            // A deleted account's tokens are refused, so only a request racing the deletion gets here: it's done.
            return;
        }

        // An admin's account holds the site's moderation; it is removed by the team, not from the app.
        if (await userManager.IsInRoleAsync(user, UserRoles.Admin))
        {
            throw new ForbidException("لا يمكن حذف حساب مشرف من هنا، تواصل مع فريق سرد لإزالة صلاحياتك أولاً", AdminCannotDeleteAccount);
        }

        await ReauthenticateAsync(user, currentUser, request);

        // Past this point the member has confirmed: the deletion runs to the end even if they close the app meanwhile.
        var result = await accountDeletion.DeleteAsync(user.Id, CancellationToken.None);
        logger.LogInformation(
            "User {UserId} deleted their account: {NovelsHidden} novels hidden, {Balance} points forfeited, {Withdrawals} withdrawals cancelled, {Reports} reports closed, {Files} files deleted ({FilesFailed} not)",
            user.Id, result.NovelsHidden, result.ForfeitedBalance, result.WithdrawalsCancelled, result.ReportsClosed,
            result.FilesDeleted, result.FilesNotDeleted);
    }

    private async Task ReauthenticateAsync(User user, CurrentUser currentUser, DeleteAccountCommand request)
    {
        var hasPassword = user.PasswordHash != null;

        if (!string.IsNullOrEmpty(request.Password))
        {
            if (!hasPassword)
            {
                throw Failed(user, "no password on the account", "حسابك بلا كلمة مرور، أكّد الحذف بتسجيل الدخول بحساب Google");
            }
            if (!await userManager.CheckPasswordAsync(user, request.Password))
            {
                throw Failed(user, "wrong password", "كلمة المرور غير صحيحة");
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.GoogleIdToken))
        {
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await googleTokens.ValidateAsync(request.GoogleIdToken);
            }
            catch (InvalidJwtException ex)
            {
                logger.LogWarning(ex, "Account deletion by {UserId}: invalid Google ID token", user.Id);
                throw Failed(user, "invalid Google ID token", "تعذّر التحقق من حساب Google، سجّل الدخول به مرة أخرى");
            }

            var logins = await userManager.GetLoginsAsync(user);
            if (!logins.Any(l => l.LoginProvider == GoogleProvider && l.ProviderKey == payload.Subject))
            {
                throw Failed(user, "Google account not linked", "حساب Google هذا غير مرتبط بحسابك في سرد");
            }
            return;
        }

        // Nothing sent: an account that signs in with Google only may rely on the sign-in it just made.
        var now = time.GetUtcNow().UtcDateTime;
        if (!hasPassword && currentUser.TokenIssuedAt is { } issuedAt && now - issuedAt <= RecentSignIn)
        {
            return;
        }

        logger.LogInformation("Account deletion by {UserId} refused: re-authentication required", user.Id);
        throw new ForbidException(
            hasPassword
                ? "أدخل كلمة المرور لتأكيد حذف حسابك"
                : "لتأكيد حذف حسابك سجّل الدخول بحساب Google مرة أخرى، ثم أكّد الحذف خلال 10 دقائق",
            ReauthenticationRequired);
    }

    private ForbidException Failed(User user, string why, string message)
    {
        logger.LogWarning("Account deletion by {UserId} refused: {Reason}", user.Id, why);
        return new ForbidException(message, ReauthenticationFailed);
    }
}
