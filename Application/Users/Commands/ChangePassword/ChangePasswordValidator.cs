using FluentValidation;

namespace Application.Users.Commands.ChangePassword;

public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(d => d.NewPassword).NewPassword();
    }
}
