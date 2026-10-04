using Application.Users.Commands.UpdateMe;
using Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Identity;

namespace Application.Users;

/// <summary>
/// What PATCH /api/User/update-me would answer a new user name with, without saving anything (#69). GET
/// /api/User/username-available answers it, and Google sign-up keeps to it when it picks a new account's handle
/// (<see cref="GoogleUserNames"/>). The same checks through the same code, in update-me's order, and the first refusal
/// is the answer:
/// <list type="number">
/// <item>update-me's validator (<see cref="UpdateMeCommandValidator"/>) on the user name alone: empty, length and "@"
/// (InvalidUserName), the route names (ReservedUserName);</item>
/// <item>its handler: the "deleted-" prefix (ReservedUserName), then a name another account holds (UserNameTaken);</item>
/// <item>Identity's allowed characters, which update-me meets when it saves (InvalidUserName).</item>
/// </list>
/// The message is update-me's for the same refusal. update-me answers the validator's as ValidationFailed (its
/// validation problem) where this has the rule's code, and puts «تعذّر تحديث الملف الشخصي» before Identity's.
/// </summary>
public sealed class UserNameCheck(IValidator<UpdateMeCommand> updateMeValidator, UserManager<User> userManager)
{
    /// <summary>
    /// update-me's refusal of <paramref name="userName"/> as <paramref name="member"/>'s new name, or null when it would
    /// take it. A missing name is checked as an empty one.
    /// </summary>
    public async Task<UserNameRefusal?> RefusalAsync(string? userName, User member) =>
        ValidatorRefusal(userName)
        ?? UserNameRules.DeletedPrefixRefusal(userName, member.UserName)
        ?? await UserNameRules.TakenRefusalAsync(userManager, userName, member)
        ?? CharactersRefusal(userName ?? "");

    /// <summary>
    /// Whether update-me's rules allow <paramref name="userName"/> for an account that has no name yet, leaving out
    /// whether another account holds it: callers look that up for many names in one query.
    /// </summary>
    public bool AllowsForNewAccount(string userName) =>
        ValidatorRefusal(userName) is null
        && UserNameRules.DeletedPrefixRefusal(userName, currentUserName: null) is null
        && CharactersRefusal(userName) is null;

    private UserNameRefusal? ValidatorRefusal(string? userName)
    {
        // To update-me a user name left out (null) means "keep mine", which its rules skip.
        var command = new UpdateMeCommand { UserName = userName ?? "" };
        var error = updateMeValidator
            .Validate(command, options => options.IncludeProperties(nameof(UpdateMeCommand.UserName)))
            .Errors.FirstOrDefault();
        return error is null ? null : new UserNameRefusal(error.ErrorCode, error.ErrorMessage);
    }

    /// <summary>Identity's check of the characters (its UserValidator), with the options and the describer it uses.</summary>
    private UserNameRefusal? CharactersRefusal(string userName)
    {
        var allowed = userManager.Options.User.AllowedUserNameCharacters;
        if (!string.IsNullOrWhiteSpace(userName) && (string.IsNullOrEmpty(allowed) || userName.All(allowed.Contains)))
        {
            return null;
        }

        var error = userManager.ErrorDescriber.InvalidUserName(userName);
        return new UserNameRefusal(error.Code, error.Description);
    }
}
