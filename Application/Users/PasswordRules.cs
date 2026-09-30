using FluentValidation;

namespace Application.Users;

/// <summary>
/// The rules of a new password for an existing account, the same wherever one is set: update-password, reset-password
/// and set-password (#53). ASP.NET Identity's password validators (IdentityOptions.Password, at least 6 characters)
/// also run when the password is saved. Sign-up has a rule of its own (at least 6 characters).
/// </summary>
public static class PasswordRules
{
    public const int MinimumLength = 8;

    public const string RequiredMessage = "اكتب كلمة المرور الجديدة";

    public const string TooShortMessage = "يجب أن تحتوي كلمة المرور الجديدة على 8 أحرف على الأقل";

    /// <summary>Required, and at least <see cref="MinimumLength"/> characters (so an empty one is too short).</summary>
    public static IRuleBuilderOptions<T, string?> NewPassword<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotNull()
            .WithMessage(RequiredMessage)
            .MinimumLength(MinimumLength)
            .WithMessage(TooShortMessage);
}
