using Application.Validation;
using FluentValidation;

namespace Application.Users.Commands.CreateUser;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(dto => dto.UserName)
            .NotEmpty()
            .WithMessage("اختر اسم مستخدم")
            .Length(3, 20)
            .WithMessage("يجب أن يكون اسم المستخدم من 3 إلى 20 حرفًا");

        RuleFor(dto => dto.UserName)
            .Must(UserNameRules.HasNoAtSign)
            .WithMessage(UserNameRules.NoAtSignMessage);

        RuleFor(dto => dto.UserName)
            .Must(UserNameRules.IsNotReserved)
            .WithMessage(UserNameRules.ReservedMessage);

        RuleFor(dto => dto.Email)
            .EmailAddress()
            .WithMessage("البريد الإلكتروني غير صالح");

        RuleFor(dto => dto.DisplayName)
            .NotEmpty()
            .WithMessage("اكتب الاسم الذي سيظهر للقرّاء")
            .Length(3, 20)
            .WithMessage("يجب أن يكون الاسم المعروض من 3 إلى 20 حرفًا");

        RuleFor(dto => dto.Password)
            .MinimumLength(6)
            .WithMessage("يجب أن تحتوي كلمة المرور على 6 أحرف على الأقل");
        
        RuleFor(dto => dto.ProfilePhoto)
            .Must(ImageValidationUtils.IsValidImageFile)
            .When(dto => dto.ProfilePhoto!= null)
            .WithMessage("الصورة الشخصية يجب أن تكون بصيغة JPEG أو PNG أو WebP، وحجمها 5 ميغابايت أو أقل");

    }

}
