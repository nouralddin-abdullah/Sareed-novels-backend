using FluentValidation;

namespace Application.Users.Commands.SetPassword;

/// <summary>
/// The new password's rules, those of update-password and reset-password (<see cref="PasswordRules"/>).
/// <see cref="SetPasswordCommandHandler"/> runs this validator itself, after checking that the account has no password:
/// ASP.NET's automatic validation only sees <see cref="SetPasswordRequest"/>, the body the controller binds, never this
/// command.
/// </summary>
public class SetPasswordCommandValidator : AbstractValidator<SetPasswordCommand>
{
    public SetPasswordCommandValidator()
    {
        RuleFor(command => command.NewPassword).NewPassword();
    }
}
