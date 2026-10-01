using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Domain.Entities;
using Domain.Profiles;
using Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #61: a member can hide their review list or their comment list (GET /api/User/{userName}/reviews and /comments)
/// from everyone else. The settings are GET and PATCH /api/User/me/privacy, also on my-profile; a hidden list is 403
/// ListHidden for anyone but the member, after 404 UserNotFound and before the block rule's empty page; the profile says
/// whether each is hidden from its viewer, and its counts stay the real ones.
/// </summary>
public partial class ProfileListsHttpTests
{
    private const string Privacy = "/api/User/me/privacy";
    private const string ReviewsHiddenMessage = "اختار صاحب الحساب إخفاء مراجعاته";
    private const string CommentsHiddenMessage = "اختار صاحب الحساب إخفاء تعليقاته";

    private Task<HttpResponseMessage> SetPrivacy(ApiUser member, object body) =>
        api.Send(HttpMethod.Patch, Privacy, member, JsonContent.Create(body));

    /// <summary>A PATCH whose body is this JSON text, as sent.</summary>
    private Task<HttpResponseMessage> SetPrivacyRaw(ApiUser member, string json) =>
        api.Send(HttpMethod.Patch, Privacy, member, new StringContent(json, Encoding.UTF8, "application/json"));

    private static void AssertSettings(JsonElement settings, string reviews, string comments)
    {
        Assert.Equal(["reviews", "comments"], Names(settings));
        Assert.Equal((reviews, comments), (settings.GetProperty("reviews").GetString(), settings.GetProperty("comments").GetString()));
    }

    private async Task AssertSaved(ApiUser member, string reviews, string comments)
    {
        AssertSettings(await Page(Privacy, member), reviews, comments);
        var profile = await Page("/api/User/my-profile", member);
        Assert.Equal((reviews, comments),
            (profile.GetProperty("reviewsVisibility").GetString(), profile.GetProperty("commentsVisibility").GetString()));
    }

    [Fact]
    public async Task Both_lists_show_to_everyone_until_the_member_changes_either_or_both_in_any_letter_case()
    {
        var (member, other) = (await api.SignUp(), await api.SignUp());
        await AssertSaved(member, "Everyone", "Everyone");

        // One at a time, in any letter case, answered and kept in the canonical spelling; the other stays.
        AssertSettings(await (await SetPrivacy(member, new { reviews = "onlyme" })).OkJson(), "OnlyMe", "Everyone");
        await AssertSaved(member, "OnlyMe", "Everyone");
        AssertSettings(await (await SetPrivacy(member, new { comments = "ONLYME" })).OkJson(), "OnlyMe", "OnlyMe");
        await AssertSaved(member, "OnlyMe", "OnlyMe");
        // Both at once; null keeps a setting as a field left out does.
        AssertSettings(await (await SetPrivacy(member, new { reviews = "everyone", comments = (string?)null })).OkJson(), "Everyone", "OnlyMe");
        await AssertSaved(member, "Everyone", "OnlyMe");
        AssertSettings(await (await SetPrivacy(member, new { reviews = "OnlyMe", comments = "Everyone" })).OkJson(), "OnlyMe", "Everyone");
        await AssertSaved(member, "OnlyMe", "Everyone");

        // An empty object, or no body at all, changes nothing and answers the settings.
        AssertSettings(await (await SetPrivacyRaw(member, "{}")).OkJson(), "OnlyMe", "Everyone");
        AssertSettings(await (await api.Send(HttpMethod.Patch, Privacy, member)).OkJson(), "OnlyMe", "Everyone");
        await AssertSaved(member, "OnlyMe", "Everyone");

        // Only the caller's own settings change.
        await AssertSaved(other, "Everyone", "Everyone");

        // Editing the profile keeps them, and its answer (my-profile's) carries them.
        var edited = await (await api.Send(HttpMethod.Patch, "/api/User/update-me", member, ReaderApi.Form(("UserBio", "نبذة")))).OkJson();
        Assert.Equal("OnlyMe", edited.GetProperty("profile").GetProperty("reviewsVisibility").GetString());
        Assert.Equal("Everyone", edited.GetProperty("profile").GetProperty("commentsVisibility").GetString());
        await AssertSaved(member, "OnlyMe", "Everyone");
    }

    [Fact]
    public async Task Any_other_value_is_refused_and_changes_nothing_and_signed_out_callers_are_refused()
    {
        var member = await api.SignUp();
        await Ok(await SetPrivacy(member, new { comments = "OnlyMe" }));

        foreach (var (body, field, message) in new[]
                 {
                     ("""{"reviews":"Followers"}""", "Reviews", "قيمة ظهور المراجعات غير صالحة. القيم الممكنة: Everyone أو OnlyMe"),
                     ("""{"comments":""}""", "Comments", "قيمة ظهور التعليقات غير صالحة. القيم الممكنة: Everyone أو OnlyMe"),
                     ("""{"reviews":"1"}""", "Reviews", "قيمة ظهور المراجعات غير صالحة. القيم الممكنة: Everyone أو OnlyMe"),
                     // A valid value next to an invalid one isn't saved either.
                     ("""{"reviews":"OnlyMe","comments":"Friends"}""", "Comments", "قيمة ظهور التعليقات غير صالحة. القيم الممكنة: Everyone أو OnlyMe")
                 })
        {
            var error = await (await SetPrivacyRaw(member, body)).Error(HttpStatusCode.BadRequest);
            Assert.Equal("ValidationFailed", error.GetProperty("code").GetString());
            Assert.Equal(message, error.GetProperty("message").GetString());
            Assert.Equal([message], error.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()));
        }

        // A value that isn't text is refused while the body is read.
        var wrongType = await (await SetPrivacyRaw(member, """{"reviews":1}""")).Error(HttpStatusCode.BadRequest);
        Assert.Equal("ValidationFailed", wrongType.GetProperty("code").GetString());
        Assert.True(wrongType.GetProperty("errors").TryGetProperty("$.reviews", out _), wrongType.ToString());

        await AssertSaved(member, "Everyone", "OnlyMe");

        // Signed out, neither reading nor changing them.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get(Privacy)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.Send(HttpMethod.Patch, Privacy, null, JsonContent.Create(new { reviews = "OnlyMe" }))).StatusCode);
        await AssertSaved(member, "Everyone", "OnlyMe");
    }

    [Fact]
    public async Task A_hidden_list_is_refused_to_everyone_but_the_member_whatever_the_other_list_and_the_counts_stay()
    {
        var (member, other, blocking, blocked, author) =
            (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var admin = await api.SignUpAdmin();
        var (novel, chapter, _) = await ReadableNovel(author);
        var review = await api.Review(member, novel.Id);
        var comment = await api.Comment(member, OnChapter(chapter.Id));
        // A member in a block either way: one who blocked the member (who still opens the profile, flagged), and one
        // the member blocked (to whom the profile is a 404).
        await Ok(await api.Block(blocking, member));
        await Ok(await api.Block(member, blocked));

        foreach (var reviews in new[] { "Everyone", "OnlyMe" })
        {
            foreach (var comments in new[] { "Everyone", "OnlyMe" })
            {
                AssertSettings(await (await SetPrivacy(member, new { reviews, comments })).OkJson(), reviews, comments);
                var (reviewsHidden, commentsHidden) = (reviews == "OnlyMe", comments == "OnlyMe");

                // The member always has both, in full, and nothing is hidden from them.
                await AssertList(ReviewsOf(member.UserName), member, false, ReviewsHiddenMessage, [review]);
                await AssertList(CommentsOf(member.UserName), member, false, CommentsHiddenMessage, [comment]);
                await AssertProfile(member, false, false);

                // Anyone else, signed in or not, an admin too: a hidden list is refused, the other one listed.
                foreach (var someone in new[] { null, other, admin })
                {
                    await AssertList(ReviewsOf(member.UserName), someone, reviewsHidden, ReviewsHiddenMessage, [review]);
                    await AssertList(CommentsOf(member.UserName), someone, commentsHidden, CommentsHiddenMessage, [comment]);
                    await AssertProfile(someone, reviewsHidden, commentsHidden);
                }

                // In a block, either way, a hidden list is refused first, and the other one is the block's empty page.
                foreach (var inBlock in new[] { blocking, blocked })
                {
                    await AssertList(ReviewsOf(member.UserName), inBlock, reviewsHidden, ReviewsHiddenMessage, []);
                    await AssertList(CommentsOf(member.UserName), inBlock, commentsHidden, CommentsHiddenMessage, []);
                }
                await AssertProfile(blocking, reviewsHidden, commentsHidden);
                Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/User/{member.UserName}", blocked)).StatusCode);

                // The counts are the real ones for everyone (the owner's decision, checked with each profile above), the
                // totals of the lists as the member has them; my-profile's too.
                var mine = await Page("/api/User/my-profile", member);
                Assert.Equal((1, 1), (mine.GetProperty("reviewsCount").GetInt32(), mine.GetProperty("commentsCount").GetInt32()));
            }
        }

        async Task AssertList(string list, ApiUser? viewer, bool hidden, string message, List<Guid> listed)
        {
            var response = await api.Get(list, viewer);
            if (hidden)
            {
                var error = await response.Error(HttpStatusCode.Forbidden);
                Assert.Equal("ListHidden", error.GetProperty("code").GetString());
                Assert.Equal(message, error.GetProperty("message").GetString());
            }
            else
            {
                var page = await response.OkJson();
                Assert.Equal(listed, page.Ids());
                Assert.Equal(listed.Count, page.GetProperty("totalItemsCount").GetInt32());
            }
        }

        async Task AssertProfile(ApiUser? viewer, bool reviewsHidden, bool commentsHidden)
        {
            var profile = await Page($"/api/User/{member.UserName}", viewer);
            Assert.Equal((reviewsHidden, commentsHidden),
                (profile.GetProperty("reviewsHidden").GetBoolean(), profile.GetProperty("commentsHidden").GetBoolean()));
            Assert.Equal((1, 1), (profile.GetProperty("reviewsCount").GetInt32(), profile.GetProperty("commentsCount").GetInt32()));
        }
    }

    [Fact]
    public async Task An_unknown_or_deleted_member_is_not_found_before_a_hidden_list_and_an_old_name_finds_it_hidden()
    {
        var (member, leaving) = (await api.SignUp(), await api.SignUp());
        foreach (var someone in new[] { member, leaving })
        {
            await Ok(await SetPrivacy(someone, new { reviews = "OnlyMe", comments = "OnlyMe" }));
        }

        // Renamed: the old name finds the member, and their lists are still hidden.
        var oldName = member.UserName;
        var newName = "r" + Guid.NewGuid().ToString("N")[..10];
        await Ok(await api.Send(HttpMethod.Patch, "/api/User/update-me", member, ReaderApi.Form(("UserName", newName))));
        foreach (var list in new[] { ReviewsOf(oldName), CommentsOf(oldName), ReviewsOf(newName), CommentsOf(newName) })
        {
            Assert.Equal("ListHidden", (await (await api.Get(list)).Error(HttpStatusCode.Forbidden)).GetProperty("code").GetString());
        }

        // A deleted account, or a name nobody has, is 404 UserNotFound, never 403.
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, "/api/User/me", leaving,
            JsonContent.Create(new { password = ModerationApi.Password }))).StatusCode);
        foreach (var name in new[] { leaving.UserName, "nobody" + Guid.NewGuid().ToString("N")[..10] })
        {
            foreach (var list in new[] { ReviewsOf(name), CommentsOf(name) })
            {
                var error = await (await api.Get(list)).Error(HttpStatusCode.NotFound);
                Assert.Equal("UserNotFound", error.GetProperty("code").GetString());
            }
        }
    }

    [Fact]
    public async Task Saving_the_settings_writes_only_them_so_an_update_that_read_the_account_before_cannot_undo_them()
    {
        var member = await api.SignUp();
        using var scope = api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var repository = scope.ServiceProvider.GetRequiredService<IUsersRepository>();
        // What a request that read the account before (update-me) holds.
        var readBefore = (await users.FindByIdAsync(member.Id))!;

        // Meanwhile counters change in SQL, as they do elsewhere.
        await using (var db = api.Db())
        {
            await db.Users.Where(u => u.Id == member.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LibraryNovelsCount, 7)
                .SetProperty(u => u.PointBalance, 50m));
        }

        // Nothing to change writes nothing.
        var before = await Row();
        Assert.Equal(new ProfileListPrivacy(ListVisibility.Everyone, ListVisibility.Everyone),
            await repository.SetListPrivacyAsync(member.Id, null, null));
        Assert.Equal(before.ConcurrencyStamp, (await Row()).ConcurrencyStamp);

        Assert.Equal(new ProfileListPrivacy(ListVisibility.OnlyMe, ListVisibility.Everyone),
            await repository.SetListPrivacyAsync(member.Id, ListVisibility.OnlyMe, null));
        var row = await Row();
        Assert.Equal((ListVisibility.OnlyMe, ListVisibility.Everyone), (row.ReviewsVisibility, row.CommentsVisibility));
        Assert.Equal((7, 50m), (row.LibraryNovelsCount, row.PointBalance));
        Assert.NotEqual(before.ConcurrencyStamp, row.ConcurrencyStamp);

        // That request's save fails on the new concurrency stamp instead of writing the old settings back.
        readBefore.UserBio = "نبذة";
        var update = await users.UpdateAsync(readBefore);
        Assert.Equal("ConcurrencyFailure", Assert.Single(update.Errors).Code);
        Assert.Equal(ListVisibility.OnlyMe, (await Row()).ReviewsVisibility);

        // A deleted account has no settings to read or change.
        await using (var db = api.Db())
        {
            await db.Users.Where(u => u.Id == member.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, DateTime.UtcNow));
        }
        Assert.Null(await repository.GetListPrivacyAsync(member.Id));
        Assert.Null(await repository.SetListPrivacyAsync(member.Id, ListVisibility.Everyone, null));
        Assert.Equal(ListVisibility.OnlyMe, (await Row()).ReviewsVisibility);

        async Task<User> Row()
        {
            await using var db = api.Db();
            return await db.Users.AsNoTracking().SingleAsync(u => u.Id == member.Id);
        }
    }
}
