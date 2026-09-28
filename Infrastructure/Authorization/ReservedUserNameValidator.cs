using Application.Users;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Authorization;

/// <summary>
/// Keeps the deleted-account user name prefix (<see cref="UserNameRules.LooksDeleted"/>) for deleted accounts: a new
/// account (sign-up, Google sign-up) or a rename to such a name is refused with <see cref="UserNameRules.DeletedPrefixCode"/>.
/// Runs on every UserManager create and update. A live account that already had such a name before this rule keeps
/// it through other changes (a case-only change included). Account deletion renames without UserManager
/// (AccountDeletionService).
/// </summary>
public sealed class ReservedUserNameValidator(ApplicationDbContext dbContext) : IUserValidator<User>
{
    public static IdentityError Error() => new() { Code = UserNameRules.DeletedPrefixCode, Description = UserNameRules.DeletedPrefixMessage };

    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user)
    {
        if (!UserNameRules.LooksDeleted(user.UserName) || user.DeletedAt is not null)
        {
            return IdentityResult.Success;
        }

        // The name the account has now: from the loaded row when this context tracks it, else from the database (none
        // for a new account).
        var entry = dbContext.Entry(user);
        var savedName = entry.State is EntityState.Detached or EntityState.Added
            ? await dbContext.Users.AsNoTracking().Where(u => u.Id == user.Id).Select(u => u.UserName).SingleOrDefaultAsync()
            : entry.Property(u => u.UserName).OriginalValue;

        return string.Equals(savedName, user.UserName, StringComparison.OrdinalIgnoreCase)
            ? IdentityResult.Success
            : IdentityResult.Failed(Error());
    }
}
