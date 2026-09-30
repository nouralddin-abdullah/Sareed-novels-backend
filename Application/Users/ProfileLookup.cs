using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Microsoft.AspNetCore.Identity;

namespace Application.Users;

/// <summary>
/// The member a profile link names (/profile/{userName}): one definition for GET /api/User/{userName} and the lists on
/// the profile (#54), so they find the same member or none.
/// </summary>
internal static class ProfileLookup
{
    public const string NotFoundCode = "UserNotFound";
    public const string NotFoundMessage = "المستخدم غير موجود";

    /// <summary>The answer for a user name that names no member (404 UserNotFound «المستخدم غير موجود»).</summary>
    public static NotFoundException NotFound() => new(NotFoundMessage, NotFoundCode);

    /// <summary>
    /// The live member with <paramref name="userName"/> (any letter case), or else the one who used it before (an old
    /// link after a rename; a live user with the name always wins). A deleted account is never found: it has no
    /// profile, and its old names were removed with it. Nobody found throws <see cref="NotFound"/>.
    /// </summary>
    public static async Task<User> FindMemberAsync(UserManager<User> userManager, IUsersRepository users,
        string userName, CancellationToken cancellationToken)
    {
        var current = await userManager.FindByNameAsync(userName);
        return (current?.DeletedAt == null ? current : null)
            ?? await users.GetByPreviousUserNameAsync(userName, cancellationToken)
            ?? throw NotFound();
    }
}
