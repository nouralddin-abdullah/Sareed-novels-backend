using Domain.Constants;
using Domain.Entities;
using FluentValidation;

namespace Application.Notifications.Commands.RegisterDevice;

public class RegisterDeviceCommandValidator : AbstractValidator<RegisterDeviceCommand>
{
    public RegisterDeviceCommandValidator()
    {
        RuleFor(x => x.Token)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("رمز الجهاز مطلوب")
            .Must(token => token.Trim().Length <= UserDevice.TokenMaxLength).WithMessage("رمز الجهاز طويل جدًا")
            // FCM tokens are letters, digits and -_: ; anything that can't sit in a URL path segment
            // (DELETE /api/notifications/devices/{token}) isn't a token.
            .Must(BeUsableInAPath).WithMessage("رمز الجهاز غير صالح");

        RuleFor(x => x.Platform)
            .Must(platform => platform is not null && DevicePlatforms.All.Contains(platform.Trim().ToLowerInvariant()))
            .WithMessage("المنصة يجب أن تكون android أو ios");

        RuleFor(x => x.AppVersion)
            .MaximumLength(32).WithMessage("رقم إصدار التطبيق طويل جدًا");

        RuleFor(x => x.Locale)
            .MaximumLength(16).WithMessage("رمز اللغة غير صالح");
    }

    private static bool BeUsableInAPath(string token) =>
        token.Trim().All(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && c is not ('/' or '\\' or '?' or '#' or '%'));
}
