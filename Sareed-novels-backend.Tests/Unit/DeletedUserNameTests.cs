using Application.Services;
using Application.Users;
using Application.Users.Commands.GoogleLogin;
using Application.Users.Commands.UpdateMe;
using AutoMapper;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Authorization;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Deleted accounts are named "deleted-" and a short id. No live account may take such a name, so clients can tell a
/// deleted author by the user name alone: sign-up, Google sign-up and renames are refused with code ReservedUserName.
/// </summary>
public class DeletedUserNameTests
{
    [Theory]
    [InlineData("deleted-3f2a9c1b7d4e")]
    [InlineData("deleted-")]
    [InlineData("Deleted-Noor")]
    [InlineData("DELETED-x")]
    [InlineData(" deleted-x")]
    public void Names_that_start_like_a_deleted_accounts_look_deleted(string userName)
    {
        Assert.True(UserNameRules.LooksDeleted(userName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("deleted")]
    [InlineData("deletedx")]
    [InlineData("undeleted-x")]
    [InlineData("deleted_x")]
    [InlineData("noor-deleted-")]
    public void Other_names_do_not(string? userName)
    {
        Assert.False(UserNameRules.LooksDeleted(userName));
    }

    [Fact]
    public void The_refusal_has_a_stable_code_and_says_why_in_arabic()
    {
        Assert.Equal("deleted-", DeletedAccounts.UserNamePrefix);
        Assert.Equal("ReservedUserName", UserNameRules.DeletedPrefixCode);
        Assert.Contains("deleted-", UserNameRules.DeletedPrefixMessage);
        Assert.Matches(@"\p{IsArabic}", UserNameRules.DeletedPrefixMessage);
        Assert.Equal(UserNameRules.DeletedPrefixCode, ReservedUserNameValidator.Error().Code);
    }

    [Theory]
    [InlineData("google-subject-1")]
    [InlineData("104857392017463829105")]
    public void Google_sign_up_never_draws_such_a_name(string subject)
    {
        for (var attempt = 0; attempt < GoogleLoginCommandHandler.UserNameAttempts; attempt++)
        {
            Assert.False(UserNameRules.LooksDeleted(GoogleLoginCommandHandler.CandidateUserName(subject, attempt)));
        }
    }

    // ---- The Identity user validator (every UserManager create and update) ----

    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=unused;Database=unused").Options);

    private static User Live(string userName) => new() { Id = Guid.NewGuid().ToString(), UserName = userName, DisplayName = "قارئ" };

    private static async Task<IdentityResult> Validate(ApplicationDbContext db, User user) =>
        await new ReservedUserNameValidator(db).ValidateAsync(null!, user);

    [Fact]
    public async Task A_rename_of_a_saved_account_to_such_a_name_is_refused()
    {
        await using var db = Context();
        var user = Live("noor");
        db.Attach(user);

        user.UserName = "deleted-noor";
        var result = await Validate(db, user);

        Assert.False(result.Succeeded);
        Assert.Equal("ReservedUserName", Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData("deleted-noor")]
    [InlineData("Deleted-Noor")]
    public async Task An_account_that_already_had_such_a_name_keeps_it_through_other_changes(string current)
    {
        await using var db = Context();
        var user = Live("deleted-noor");
        db.Attach(user);

        user.UserName = current;
        user.UserBio = "نبذة جديدة";

        Assert.True((await Validate(db, user)).Succeeded);
    }

    [Fact]
    public async Task Other_names_and_deleted_accounts_pass_without_asking_the_database()
    {
        // Detached: a new account, whose saved name the validator would read from the (unreachable) database.
        await using var db = Context();

        Assert.True((await Validate(db, Live("noor-reader"))).Succeeded);
        var deleted = Live("deleted-3f2a9c1b7d4e");
        deleted.DeletedAt = DateTime.UtcNow;
        Assert.True((await Validate(db, deleted)).Succeeded);
    }

    // ---- update-me says so with its code, before anything is uploaded or saved ----

    [Theory]
    [InlineData("deleted-noor")]
    [InlineData("Deleted-X")]
    public async Task Update_me_refuses_a_new_name_that_starts_like_a_deleted_accounts(string userName)
    {
        var user = Live("noor");
        var users = Substitute.For<UserManager<User>>(Substitute.For<IUserStore<User>>(), null, null, null, null, null, null, null, null);
        users.FindByIdAsync(user.Id).Returns(user);
        var userContext = Substitute.For<IUserContext>();
        userContext.GetCurrentUser().Returns(new CurrentUser(user.Id, "noor@example.test", user.UserName!, user.DisplayName));
        var uploads = Substitute.For<IFileUploadService>();
        var handler = new UpdateMeCommandHandler(NullLogger<UpdateMeCommandHandler>.Instance, uploads, userContext, users, Substitute.For<IMapper>());

        var result = await handler.Handle(new UpdateMeCommand { UserName = userName, UserBio = "نبذة" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(UserNameRules.DeletedPrefixCode, result.Code);
        Assert.Equal(UserNameRules.DeletedPrefixMessage, result.Message);
        await users.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        await users.DidNotReceiveWithAnyArgs().FindByNameAsync(default!);
        Assert.Empty(uploads.ReceivedCalls());
    }
}
