using System.Data;
using FluentValidation;

namespace Application.Users.Commands.ChangePassword;

public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(d => d.NewPassword)
            .NotNull()
            .WithMessage("اكتب كلمة المرور الجديدة")
            .MinimumLength(8)
            .WithMessage("يجب أن تحتوي كلمة المرور الجديدة على 8 أحرف على الأقل");
    }
}
