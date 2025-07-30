using FluentValidation;

namespace Application.Users.Commands.ResetPassword;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(d => d.NewPassword)
            .NotNull()
            .MinimumLength(8)
            .WithMessage("New password cannot be empty or less than 8 characters.");

        RuleFor(d => d.UserId)
            .NotNull();

        RuleFor(d => d.Token)
            .NotNull();
    }
}
