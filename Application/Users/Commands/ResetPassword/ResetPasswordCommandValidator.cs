using FluentValidation;

namespace Application.Users.Commands.ResetPassword;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(d => d.NewPassword).NewPassword();

        // Both come from the link in the email.
        RuleFor(d => d.UserId)
            .NotNull()
            .WithMessage("رابط تعيين كلمة المرور ناقص. اطلب رابطًا جديدًا.");

        RuleFor(d => d.Token)
            .NotNull()
            .WithMessage("رابط تعيين كلمة المرور ناقص. اطلب رابطًا جديدًا.");
    }
}
