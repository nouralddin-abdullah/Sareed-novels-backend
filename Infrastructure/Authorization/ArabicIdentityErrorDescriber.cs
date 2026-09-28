using Application.Common;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Authorization;

/// <summary>
/// ASP.NET Identity's errors in Arabic, worded like the Sard app. Clients read them as <c>{code, description}</c>
/// (register, confirm email, reset and change password, profile updates): every code stays Identity's own, only the
/// description changes.
/// </summary>
public sealed class ArabicIdentityErrorDescriber : IdentityErrorDescriber
{
    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };

    public override IdentityError DefaultError() =>
        Error(nameof(DefaultError), "حدث خطأ غير متوقع. حاول مرة أخرى.");

    public override IdentityError ConcurrencyFailure() =>
        Error(nameof(ConcurrencyFailure), "تغيّرت بيانات الحساب أثناء الحفظ. حاول مرة أخرى.");

    public override IdentityError PasswordMismatch() =>
        Error(nameof(PasswordMismatch), "كلمة المرور الحالية غير صحيحة");

    public override IdentityError InvalidToken() =>
        Error(nameof(InvalidToken), "الرابط غير صالح أو انتهت صلاحيته. اطلب رابطًا جديدًا.");

    public override IdentityError RecoveryCodeRedemptionFailed() =>
        Error(nameof(RecoveryCodeRedemptionFailed), "رمز الاسترداد غير صحيح");

    // Google is the only external sign-in.
    public override IdentityError LoginAlreadyAssociated() =>
        Error(nameof(LoginAlreadyAssociated), "حساب Google هذا مرتبط بحساب آخر في سرد");

    public override IdentityError InvalidUserName(string? userName) =>
        Error(nameof(InvalidUserName), "اسم المستخدم يجب أن يتكوّن من أحرف إنجليزية وأرقام فقط، ويمكن إضافة الرموز - . _ +");

    public override IdentityError InvalidEmail(string? email) =>
        Error(nameof(InvalidEmail), "البريد الإلكتروني غير صالح");

    public override IdentityError DuplicateUserName(string userName) =>
        Error(nameof(DuplicateUserName), "اسم المستخدم مستخدم بالفعل، اختر اسمًا آخر");

    public override IdentityError DuplicateEmail(string email) =>
        Error(nameof(DuplicateEmail), "هذا البريد مستخدم في حساب آخر. سجّل الدخول به أو استعد كلمة المرور.");

    public override IdentityError InvalidRoleName(string? role) =>
        Error(nameof(InvalidRoleName), $"اسم الدور «{role}» غير صالح");

    public override IdentityError DuplicateRoleName(string role) =>
        Error(nameof(DuplicateRoleName), $"الدور «{role}» موجود بالفعل");

    public override IdentityError UserAlreadyHasPassword() =>
        Error(nameof(UserAlreadyHasPassword), "لهذا الحساب كلمة مرور بالفعل");

    public override IdentityError UserLockoutNotEnabled() =>
        Error(nameof(UserLockoutNotEnabled), "الإيقاف المؤقت لتسجيل الدخول غير مفعّل لهذا الحساب");

    public override IdentityError UserAlreadyInRole(string role) =>
        Error(nameof(UserAlreadyInRole), $"للمستخدم الدور «{role}» بالفعل");

    public override IdentityError UserNotInRole(string role) =>
        Error(nameof(UserNotInRole), $"ليس للمستخدم الدور «{role}»");

    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort), $"يجب أن تحتوي كلمة المرور على {ArabicCount.Letters(length)} على الأقل");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), "كلمة المرور تحتاج إلى أحرف أكثر تنوعًا");

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), "يجب أن تحتوي كلمة المرور على رمز واحد على الأقل، مثل ! أو #");

    public override IdentityError PasswordRequiresDigit() =>
        Error(nameof(PasswordRequiresDigit), "يجب أن تحتوي كلمة المرور على رقم واحد على الأقل");

    public override IdentityError PasswordRequiresLower() =>
        Error(nameof(PasswordRequiresLower), "يجب أن تحتوي كلمة المرور على حرف إنجليزي صغير واحد على الأقل");

    public override IdentityError PasswordRequiresUpper() =>
        Error(nameof(PasswordRequiresUpper), "يجب أن تحتوي كلمة المرور على حرف إنجليزي كبير واحد على الأقل");
}
