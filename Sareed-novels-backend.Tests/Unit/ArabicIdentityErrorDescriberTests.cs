using System.Reflection;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>Identity's errors reach clients as {code, description}: Arabic descriptions, Identity's own codes.</summary>
public class ArabicIdentityErrorDescriberTests
{
    public static TheoryData<string> Methods() =>
        new(typeof(IdentityErrorDescriber).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.ReturnType == typeof(IdentityError) && m.IsVirtual)
            .Select(m => m.Name));

    private static IdentityError Call(IdentityErrorDescriber describer, string name)
    {
        var method = describer.GetType().GetMethod(name)!;
        var arguments = method.GetParameters().Select(p => p.ParameterType == typeof(int) ? (object)6 : "x").ToArray();
        return (IdentityError)method.Invoke(describer, arguments)!;
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public void Every_error_is_arabic_and_keeps_identitys_code(string name)
    {
        var arabic = Call(new ArabicIdentityErrorDescriber(), name);
        var original = Call(new IdentityErrorDescriber(), name);

        Assert.Equal(original.Code, arabic.Code);
        Assert.Matches(@"\p{IsArabic}", arabic.Description);
        Assert.DoesNotMatch("[\"']", arabic.Description);
    }

    [Theory]
    [InlineData(1, "يجب أن تحتوي كلمة المرور على حرف واحد على الأقل")]
    [InlineData(2, "يجب أن تحتوي كلمة المرور على حرفين على الأقل")]
    [InlineData(6, "يجب أن تحتوي كلمة المرور على 6 أحرف على الأقل")]
    [InlineData(12, "يجب أن تحتوي كلمة المرور على 12 حرفًا على الأقل")]
    [InlineData(100, "يجب أن تحتوي كلمة المرور على 100 حرف على الأقل")]
    public void A_short_password_says_how_many_letters_it_needs(int length, string expected) =>
        Assert.Equal(expected, new ArabicIdentityErrorDescriber().PasswordTooShort(length).Description);
}
