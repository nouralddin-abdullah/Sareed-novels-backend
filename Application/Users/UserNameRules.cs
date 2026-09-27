namespace Application.Users;

/// <summary>
/// User names are public: profile links (/profile/{userName}) and the @handle shown on profiles, comments and lists.
/// An email address typed as a user name was published that way, so "@" is not allowed (sign-in also reads any
/// "@" as an email address).
/// </summary>
public static class UserNameRules
{
    public const string NoAtSignMessage = "لا يمكن أن يحتوي اسم المستخدم على الرمز @";

    public static bool HasNoAtSign(string? userName) => userName is null || !userName.Contains('@');
}
