using Domain.Constants;

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

    /// <summary>
    /// The code of a new user name that starts like a deleted account's (<see cref="DeletedAccounts.UserNamePrefix"/>),
    /// named like Identity's DuplicateUserName and InvalidUserName. Register answers it in <c>result.code</c> as it
    /// answers those, update-me in <c>code</c> as it answers UserNameTaken.
    /// </summary>
    public const string DeletedPrefixCode = "ReservedUserName";

    public const string DeletedPrefixMessage = "لا يمكن أن يبدأ اسم المستخدم بـ deleted-، فهذه البداية محجوزة للحسابات المحذوفة";

    /// <summary>
    /// Whether <paramref name="userName"/> starts like a deleted account's ("deleted-3f2a9c1b7d4e", ignoring case), so
    /// clients can tell a deleted author by the name alone. No live account may take such a name: sign-up, Google
    /// sign-up and renames are refused where the account is saved (ReservedUserNameValidator, an Identity user
    /// validator, so no path can skip it), and update-me says so first.
    /// </summary>
    public static bool LooksDeleted(string? userName) =>
        userName is not null && userName.Trim().StartsWith(DeletedAccounts.UserNamePrefix, StringComparison.OrdinalIgnoreCase);
}
