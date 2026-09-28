using Application.Validation;
using FluentValidation;

namespace Application.Users.Commands.UpdateMe;

public class UpdateMeCommandValidator : AbstractValidator<UpdateMeCommand>
{
    public UpdateMeCommandValidator()
    {
        RuleFor(dto => dto.UserName)
            .NotEmpty()
            .WithMessage("اختر اسم مستخدم")
            .Length(3, 20)
            .WithMessage("يجب أن يكون اسم المستخدم من 3 إلى 20 حرفًا")
            .When(dto => dto.UserName != null);

        RuleFor(dto => dto.UserName)
            .Must(UserNameRules.HasNoAtSign)
            .WithMessage(UserNameRules.NoAtSignMessage);

        RuleFor(dto => dto.UserName)
            .Must(UserNameRules.IsNotReserved)
            .WithMessage(UserNameRules.ReservedMessage);

        RuleFor(dto => dto.DisplayName)
           .NotEmpty()
           .WithMessage("اكتب الاسم الذي سيظهر للقرّاء")
           .Length(3, 20)
           .WithMessage("يجب أن يكون الاسم المعروض من 3 إلى 20 حرفًا")
           .When(dto => dto.DisplayName != null);

        RuleFor(dto => dto.ProfilePhoto)
           .Must(ImageValidationUtils.IsValidImageFile)
           .When(dto => dto.ProfilePhoto != null)
           .WithMessage("الصورة الشخصية يجب أن تكون بصيغة JPEG أو PNG أو WebP، وحجمها 5 ميغابايت أو أقل");

        RuleFor(dto => dto.ProfileBanner)
           .Must(ImageValidationUtils.IsValidImageFile)
           .When(dto => dto.ProfileBanner != null)
           .WithMessage("صورة الغلاف يجب أن تكون بصيغة JPEG أو PNG أو WebP، وحجمها 5 ميغابايت أو أقل");

        RuleFor(dto => dto.UserBio)
            .MaximumLength(150)
            .WithMessage("يجب ألا تتجاوز النبذة 150 حرفًا");

        // Profile pages render these as links: only http(s) addresses (or scheme-less ones like facebook.com/x),
        // never javascript: or other schemes.
        RuleFor(dto => dto.FacebookUrl)
            .MaximumLength(300)
            .WithMessage("يجب ألا يتجاوز رابط فيسبوك 300 حرف")
            .Must(BeWebLink)
            .WithMessage("رابط فيسبوك يجب أن يكون رابط صفحة ويب");

        RuleFor(dto => dto.TwitterUrl)
            .MaximumLength(300)
            .WithMessage("يجب ألا يتجاوز رابط إكس 300 حرف")
            .Must(BeWebLink)
            .WithMessage("رابط إكس يجب أن يكون رابط صفحة ويب");

        RuleFor(dto => dto.DiscordUrl)
            .MaximumLength(300)
            .WithMessage("يجب ألا يتجاوز اسم ديسكورد 300 حرف");
    }

    internal static bool BeWebLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains(':'))
        {
            return true;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
