using Domain.Constants;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.Users;

/// <summary>
/// User names are public: profile links (/profile/{userName}) and the @handle shown on profiles, comments and lists.
/// An email address typed as a user name was published that way, so "@" is not allowed (sign-in also reads any
/// "@" as an email address).
/// </summary>
/// <remarks>
/// The rules a new user name meets in PATCH /api/User/update-me (#69): its validator (length, "@", reserved names), its
/// handler (the "deleted-" prefix, a name another account holds) and Identity's allowed characters when it saves.
/// GET /api/User/username-available (<see cref="UserNameCheck"/>) and Google sign-up (<see cref="GoogleUserNames"/>)
/// keep to them through the same code.
/// </remarks>
public static class UserNameRules
{
    /// <summary>The shortest user name update-me (and sign-up) accept.</summary>
    public const int MinLength = 3;

    /// <summary>The longest user name update-me (and sign-up) accept.</summary>
    public const int MaxLength = 20;

    /// <summary>
    /// ASP.NET Identity's AllowedUserNameCharacters (set from this in Infrastructure): Identity's default set without
    /// "@", since user names are public and email addresses used as user names were published that way (the
    /// validators give the Arabic message; Identity's check is the backstop). Identity refuses any other character
    /// with <see cref="InvalidCode"/> when an account is saved.
    /// </summary>
    public const string AllowedCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._+";

    /// <summary>
    /// The code of a user name that breaks a rule of its own: empty, too short or long, with "@" or with a character
    /// Identity doesn't allow. Identity's own code for the last of those (IdentityErrorDescriber.InvalidUserName).
    /// </summary>
    public const string InvalidCode = "InvalidUserName";

    /// <summary>The code of a user name another account holds, in any letter case (update-me's).</summary>
    public const string TakenCode = "UserNameTaken";

    /// <summary>The code of a reserved user name: a route name (<see cref="IsNotReserved"/>) or the "deleted-" prefix.</summary>
    public const string ReservedCode = "ReservedUserName";

    public const string RequiredMessage = "اختر اسم مستخدم";

    /// <summary>A user name shorter than <see cref="MinLength"/> or longer than <see cref="MaxLength"/>.</summary>
    public const string LengthMessage = "يجب أن يكون اسم المستخدم من 3 إلى 20 حرفًا";

    public const string TakenMessage = "اسم المستخدم مستخدم بالفعل، اختر اسمًا آخر";

    public const string NoAtSignMessage = "لا يمكن أن يحتوي اسم المستخدم على الرمز @";

    public static bool HasNoAtSign(string? userName) => userName is null || !userName.Contains('@');

    public const string ReservedMessage = "اسم المستخدم هذا محجوز، اختر اسماً آخر";

    /// <summary>
    /// Names that are also routes next to GET /api/User/{userName} (routes match them first, whatever the case), so
    /// a profile with such a name could never be opened: "blocked", "my-profile" and "username-available" (#69); and
    /// next to its lists {userName}/reviews and {userName}/comments (#54), which "followers-list/{userId}" and
    /// "following-list/{userId}" would win.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "blocked", "my-profile", "username-available", "followers-list", "following-list"
    };

    public static bool IsNotReserved(string? userName) => userName is null || !Reserved.Contains(userName.Trim());

    /// <summary>
    /// The code of a new user name that starts like a deleted account's (<see cref="DeletedAccounts.UserNamePrefix"/>),
    /// named like Identity's DuplicateUserName and InvalidUserName. Register answers it in <c>result.code</c> as it
    /// answers those, update-me in <c>code</c> as it answers UserNameTaken.
    /// </summary>
    public const string DeletedPrefixCode = ReservedCode;

    public const string DeletedPrefixMessage = "لا يمكن أن يبدأ اسم المستخدم بـ deleted-، فهذه البداية محجوزة للحسابات المحذوفة";

    /// <summary>
    /// Whether <paramref name="userName"/> starts like a deleted account's ("deleted-3f2a9c1b7d4e", ignoring case), so
    /// clients can tell a deleted author by the name alone. No live account may take such a name: sign-up, Google
    /// sign-up and renames are refused where the account is saved (ReservedUserNameValidator, an Identity user
    /// validator, so no path can skip it), and update-me says so first.
    /// </summary>
    public static bool LooksDeleted(string? userName) =>
        userName is not null && userName.Trim().StartsWith(DeletedAccounts.UserNamePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// update-me's refusal of a user name that starts like a deleted account's (<see cref="LooksDeleted"/>), unless it
    /// is the member's own name in any letter case (an account named so before the rule keeps its name). Null for a
    /// new account's name, which has no current one.
    /// </summary>
    public static UserNameRefusal? DeletedPrefixRefusal(string? userName, string? currentUserName) =>
        LooksDeleted(userName) && !string.Equals(userName, currentUserName, StringComparison.OrdinalIgnoreCase)
            ? new UserNameRefusal(DeletedPrefixCode, DeletedPrefixMessage)
            : null;

    /// <summary>
    /// update-me's refusal of a user name another account holds now, compared as Identity compares names (its
    /// normalized form, so any letter case). The member's own name in any case isn't taken (#25), and nor is a name
    /// someone gave up: the name history (UserNameChange) only keeps old profile links working, and a member who holds
    /// a name now wins over it.
    /// </summary>
    public static async Task<UserNameRefusal?> TakenRefusalAsync(UserManager<User> userManager, string? userName, User member)
    {
        if (string.IsNullOrEmpty(userName) || userName == member.UserName)
        {
            return null;
        }

        var holder = await userManager.FindByNameAsync(userName);
        return holder != null && holder.Id != member.Id ? new UserNameRefusal(TakenCode, TakenMessage) : null;
    }
}

/// <summary>Why a user name can't be taken: a stable <paramref name="Code"/> and the Arabic <paramref name="Message"/> update-me answers.</summary>
public sealed record UserNameRefusal(string Code, string Message);
