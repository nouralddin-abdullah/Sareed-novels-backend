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

    public const string ReservedMessage = "اسم المستخدم هذا محجوز، اختر اسماً آخر";

    /// <summary>
    /// Names that are also routes next to GET /api/User/{userName} (routes match them first, whatever the case), so
    /// a profile with such a name could never be opened.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "blocked", "my-profile" };

    public static bool IsNotReserved(string? userName) => userName is null || !Reserved.Contains(userName.Trim());
}
