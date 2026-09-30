using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.SetPassword;

/// <summary>
/// POST /api/User/set-password (#53): the signed-in member gives their account its first password (an account made
/// with Google has none), in the app rather than through the reset-password email. The handler checks, in this order,
/// and the first refusal is the answer:
/// <list type="number">
/// <item>the account has no password yet, else 400 <see cref="SetPasswordCommandHandler.PasswordAlreadySet"/>
/// (update-password changes it);</item>
/// <item>the password keeps the rules of update-password and reset-password, and a refusal has their code and message:
/// <see cref="PasswordRules"/> (400 ValidationFailed), then ASP.NET Identity's password validators (400 with Identity's
/// code). They come before the proof, so nobody signs in with Google again only to learn the password is too
/// short;</item>
/// <item>the member proves it's them (<see cref="Reauthentication"/>): a Google ID token of the account's Google
/// sign-in, or a sign-in in the last 10 minutes; else 403 ReauthenticationFailed or ReauthenticationRequired;</item>
/// <item>the password is saved (204). Every session stays signed in.</item>
/// </list>
/// </summary>
public sealed record SetPasswordCommand(string? NewPassword, string? GoogleIdToken) : IRequest<IdentityResult>;

/// <summary>
/// Returns Identity's refusal of the password (the controller answers it as update-password does), or success; throws
/// the other refusals (<see cref="SetPasswordCommand"/>).
/// </summary>
public class SetPasswordCommandHandler(
    ILogger<SetPasswordCommandHandler> logger,
    IUserContext userContext,
    UserManager<User> userManager,
    IValidator<SetPasswordCommand> validator,
    Reauthentication reauthentication,
    IUsersRepository users) : IRequestHandler<SetPasswordCommand, IdentityResult>
{
    public const string PasswordAlreadySet = "PasswordAlreadySet";
    public const string PasswordAlreadySetMessage = "لحسابك كلمة مرور بالفعل، غيّرها من «تغيير كلمة المرور».";

    /// <summary>
    /// The code of a password <see cref="PasswordRules"/> refuses: the API's ValidationProblems.Code, what
    /// update-password and reset-password answer for these rules.
    /// </summary>
    public const string ValidationFailed = "ValidationFailed";

    private static readonly ReauthenticationMessages Confirmation = new(
        Action: "Setting a password",
        SignInAgain: "لتعيين كلمة مرور لحسابك سجّل الدخول بحساب Google مرة أخرى، ثم عيّنها خلال 10 دقائق");

    public async Task<IdentityResult> Handle(SetPasswordCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser()
            ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var user = await userManager.FindByIdAsync(currentUser.Id);
        if (user is null || user.DeletedAt != null)
        {
            // A deleted account's tokens are refused before this: only a request racing the deletion gets here.
            throw new NotFoundException("المستخدم غير موجود", "UserNotFound");
        }

        // 1. An account with a password changes it with update-password, which asks for the current one.
        if (user.PasswordHash != null)
        {
            throw AlreadySet(user.Id);
        }

        // 2. The rules, before the proof.
        var broken = (await validator.ValidateAsync(request, cancellationToken)).Errors.FirstOrDefault();
        if (broken is not null)
        {
            throw new BadRequestException(broken.ErrorMessage, ValidationFailed);
        }
        var password = request.NewPassword!;
        var identityRules = await ValidateWithIdentityAsync(user, password);
        if (!identityRules.Succeeded)
        {
            return identityRules;
        }

        // 3. It's them: the account has no password, so Google's proof or a recent sign-in.
        await reauthentication.ConfirmAsync(user, currentUser, request.GoogleIdToken, Confirmation);

        // 4. Saved only if the account still has no password, touching nothing else in its row.
        var hash = userManager.PasswordHasher.HashPassword(user, password);
        if (!await users.SetFirstPasswordAsync(user.Id, hash, cancellationToken))
        {
            // One was set since the check above: by a second request at the same time (a double tap), or a reset link.
            throw AlreadySet(user.Id);
        }

        logger.LogInformation("User {UserId} set a password for their account", user.Id);
        return IdentityResult.Success;
    }

    /// <summary>
    /// ASP.NET Identity's password validators (IdentityOptions.Password), every one, as UserManager runs them before it
    /// saves a password (update-password, reset-password).
    /// </summary>
    private async Task<IdentityResult> ValidateWithIdentityAsync(User user, string password)
    {
        var errors = new List<IdentityError>();
        foreach (var passwordValidator in userManager.PasswordValidators)
        {
            errors.AddRange((await passwordValidator.ValidateAsync(userManager, user, password)).Errors);
        }
        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    private BadRequestException AlreadySet(string userId)
    {
        logger.LogInformation("Setting a password refused: user {UserId} has one", userId);
        return new BadRequestException(PasswordAlreadySetMessage, PasswordAlreadySet);
    }
}
