using FluentValidation;

namespace Application.Users.Commands.ResetPassword;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(d => d.NewPassword)
            .NotNull()
            .WithMessage("اكتب كلمة المرور الجديدة")
            .MinimumLength(8)
            .WithMessage("يجب أن تحتوي كلمة المرور الجديدة على 8 أحرف على الأقل");

        // Both come from the link in the email.
        RuleFor(d => d.UserId)
            .NotNull()
            .WithMessage("رابط تعيين كلمة المرور ناقص. اطلب رابطًا جديدًا.");

        RuleFor(d => d.Token)
            .NotNull()
            .WithMessage("رابط تعيين كلمة المرور ناقص. اطلب رابطًا جديدًا.");
    }
}
