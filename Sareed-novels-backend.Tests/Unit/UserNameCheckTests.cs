using Application.Users;
using Application.Users.Commands.UpdateMe;
using Domain.Entities;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// update-me's verdict on a user name without saving (#69): its checks in its order, with the exemptions update-me
/// gives the member's own name and no others.
/// </summary>
public class UserNameCheckTests
{
    private readonly IUserStore<User> store = Substitute.For<IUserStore<User>>();
    private readonly UserNameCheck check;

    public UserNameCheckTests()
    {
        var options = Options.Create(new IdentityOptions { User = { AllowedUserNameCharacters = UserNameRules.AllowedCharacters } });
        var userManager = new UserManager<User>(store, options, null!, null!, null!, new UpperInvariantLookupNormalizer(),
            new ArabicIdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
        check = new UserNameCheck(new UpdateMeCommandValidator(), userManager);
    }

    private User Member(string userName)
    {
        var member = new User { Id = Guid.NewGuid().ToString(), UserName = userName, DisplayName = "قارئ" };
        store.FindByNameAsync(userName.ToUpperInvariant(), Arg.Any<CancellationToken>()).Returns(member);
        return member;
    }

    [Theory]
    [InlineData("deleted-noor")]
    [InlineData("Deleted-Noor")]
    public async Task A_deleted_looking_name_from_before_the_rule_stays_available_to_its_owner_only(string asTyped)
    {
        // update-me lets an account named so before the rule keep its name, in any letter case.
        var owner = Member("deleted-noor");
        var someoneElse = Member("noor-reader");

        Assert.Null(await check.RefusalAsync(asTyped, owner));
        Assert.Equal(new UserNameRefusal(UserNameRules.ReservedCode, UserNameRules.DeletedPrefixMessage),
            await check.RefusalAsync(asTyped, someoneElse));
    }

    [Fact]
    public async Task The_members_own_name_is_still_held_to_update_mes_validator()
    {
        // update-me's validator has no exemption for one's own name: a name from before the rules that breaks one is
        // refused by update-me, so the check says so too.
        var legacy = Member("a-name-from-before-the-rules");

        Assert.Equal(new UserNameRefusal(UserNameRules.InvalidCode, UserNameRules.LengthMessage),
            await check.RefusalAsync(legacy.UserName, legacy));
    }

    [Fact]
    public async Task Checks_come_in_update_mes_order()
    {
        var member = Member("noor-reader");
        var deletedLooking = Member("deleted-x1");
        var withASpace = Member("legacy name"); // held from before the rules, say

        // The validator before the "deleted-" prefix, the prefix before the lookup, the lookup before the characters.
        Assert.Equal(UserNameRules.NoAtSignMessage, (await check.RefusalAsync("deleted-@x", member))!.Message);
        Assert.Equal(UserNameRules.DeletedPrefixMessage, (await check.RefusalAsync(deletedLooking.UserName, member))!.Message);
        Assert.Equal(UserNameRules.TakenMessage, (await check.RefusalAsync(withASpace.UserName, member))!.Message);
        Assert.Equal(new ArabicIdentityErrorDescriber().InvalidUserName(null).Description,
            (await check.RefusalAsync("free name", member))!.Message);
        Assert.Null(await check.RefusalAsync("NOOR-READER", member));
    }

    [Fact]
    public void A_new_account_has_no_exemptions()
    {
        Assert.True(check.AllowsForNewAccount("noor-reader"));
        Assert.False(check.AllowsForNewAccount("deleted-noor"));
        Assert.False(check.AllowsForNewAccount("username-available"));
        Assert.False(check.AllowsForNewAccount("no"));
        Assert.False(check.AllowsForNewAccount("noor reader"));
    }
}
