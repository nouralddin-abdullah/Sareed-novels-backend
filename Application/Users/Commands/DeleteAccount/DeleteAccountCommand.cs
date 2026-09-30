using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.DeleteAccount;

/// <summary>
/// DELETE /api/User/me: the signed-in member deletes their account for good (<see cref="IAccountDeletionService"/>).
/// They prove it's them again first (<see cref="Reauthentication"/>), with one of:
/// <list type="bullet">
/// <item><see cref="Password"/>, for an account with a password;</item>
/// <item><see cref="GoogleIdToken"/>, from signing in with Google just now, for an account with a Google sign-in (the
/// token's subject must be that sign-in);</item>
/// <item>nothing, for an account without a password whose access token comes from a sign-in in the last
/// <see cref="Reauthentication.RecentSignIn"/> (the web signs in with Google by redirect and never holds an ID token,
/// so it has the member sign in again instead).</item>
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
    Reauthentication reauthentication,
    IAccountDeletionService accountDeletion,
    AccountDeletionAttempts attempts) : IRequestHandler<DeleteAccountCommand>
{
    public const string AdminCannotDeleteAccount = "AdminCannotDeleteAccount";
    public const string TooManyAttempts = "TooManyDeletionAttempts";

    private static readonly ReauthenticationMessages Confirmation = new(
        Action: "Account deletion",
        SignInAgain: "لتأكيد حذف حسابك سجّل الدخول بحساب Google مرة أخرى، ثم أكّد الحذف خلال 10 دقائق");

    private static readonly PasswordReauthenticationMessages PasswordConfirmation = new(
        Required: "أدخل كلمة المرور لتأكيد حذف حسابك",
        AccountHasNone: "حسابك بلا كلمة مرور، أكّد الحذف بتسجيل الدخول بحساب Google");

    public async Task Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة");

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

        await reauthentication.ConfirmAsync(
            user, currentUser, request.Password, request.GoogleIdToken, Confirmation, PasswordConfirmation);

        // Past this point the member has confirmed: the deletion runs to the end even if they close the app meanwhile.
        var result = await accountDeletion.DeleteAsync(user.Id, CancellationToken.None);
        logger.LogInformation(
            "User {UserId} deleted their account: {NovelsHidden} novels hidden, {Balance} points forfeited, {Withdrawals} withdrawals cancelled, {Reports} reports closed, {Files} files deleted ({FilesFailed} not)",
            user.Id, result.NovelsHidden, result.ForfeitedBalance, result.WithdrawalsCancelled, result.ReportsClosed,
            result.FilesDeleted, result.FilesNotDeleted);
    }
}
