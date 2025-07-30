using System.Data;
using FluentValidation;

namespace Application.Users.Commands.ChangePassword;

public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(d => d.NewPassword)
            .NotNull()
            .MinimumLength(8)
            .WithMessage("New password cannot be empty or less than 8 characters.");
    }
}
