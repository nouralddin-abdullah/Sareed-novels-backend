using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Moderation;
using Domain.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// DELETE /api/User/me through the real pipeline: re-authentication, the admin refusal and the attempt limit, and what
/// deleting an account does to every table that holds something of the member.
/// </summary>
public class AccountDeletionHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private const string Password = ModerationApi.Password;

    private Task<HttpResponseMessage> DeleteAccount(ApiUser user, object? body = null) =>
        api.Send(HttpMethod.Delete, "/api/User/me", user, body is null ? null : JsonContent.Create(body));

    private async Task DeleteWithPassword(ApiUser user)
    {
        var response = await DeleteAccount(user, new { password = Password });
        Assert.True(response.StatusCode == HttpStatusCode.NoContent,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private async Task<User> Row(string userId)
    {
        await using var db = api.Db();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    private static async Task<(string Code, string Message)> Refusal(HttpResponseMessage response, HttpStatusCode status)
    {
        var body = await response.Error(status);
        return (body.GetProperty("code").GetString()!, body.GetProperty("message").GetString()!);
    }

    /// <summary>A member who signed up with Google (no password), with the token of that sign-in.</summary>
    private async Task<(ApiUser User, string Email, string GoogleSubject)> SignUpWithGoogle()
    {
        var email = $"g{Guid.NewGuid():N}@example.test";
        var subject = "google-" + Guid.NewGuid().ToString("N");
        var response = await api.Client().PostAsJsonAsync("/api/identity/google-login", new { idToken = api.GoogleTokens.Issue(email, subject: subject) });
        var token = (await response.OkJson()).GetProperty("accessToken").GetString()!;

        await using var db = api.Db();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        return (new ApiUser(user.Id, user.UserName!, token), email, subject);
    }

    /// <summary>The user's access token as if issued at <paramref name="issuedAt"/>.</summary>
    private ApiUser WithTokenIssuedAt(ApiUser user, DateTime issuedAt) => user with
    {
        Token = TokenFactory.Write(user.Id, issuedAt, issuedAt.AddDays(60), api.Services.GetRequiredService<IConfiguration>()["Jwt:Key"]!)
    };

    private async Task<JsonElement> MyProfile(ApiUser user) => await (await api.Get("/api/User/my-profile", user)).OkJson();

    // ─── Re-authentication ───

    [Fact]
    public async Task A_password_account_confirms_with_its_password()
    {
        var member = await api.SignUp();
        Assert.True((await MyProfile(member)).GetProperty("hasPassword").GetBoolean());

        var (code, message) = await Refusal(await DeleteAccount(member, new { password = "not-the-password" }), HttpStatusCode.Forbidden);
        Assert.Equal("ReauthenticationFailed", code);
        Assert.Equal("كلمة المرور غير صحيحة", message);

        // Nothing sent, even from a fresh sign-in: an account with a password always confirms with it.
        (code, message) = await Refusal(await DeleteAccount(member), HttpStatusCode.Forbidden);
        Assert.Equal("ReauthenticationRequired", code);
        Assert.Equal("أدخل كلمة المرور لتأكيد حذف حسابك", message);
        (code, _) = await Refusal(await DeleteAccount(member, new { }), HttpStatusCode.Forbidden);
        Assert.Equal("ReauthenticationRequired", code);

        Assert.Null((await Row(member.Id)).DeletedAt);

        await DeleteWithPassword(member);
        Assert.NotNull((await Row(member.Id)).DeletedAt);
    }

    [Fact]
    public async Task A_Google_account_confirms_with_an_ID_token_of_the_same_Google_account()
    {
        var (member, email, subject) = await SignUpWithGoogle();
        Assert.False((await MyProfile(member)).GetProperty("hasPassword").GetBoolean());

        var otherGoogleAccount = api.GoogleTokens.Issue(email, subject: "google-someone-else");
        var (code, message) = await Refusal(await DeleteAccount(member, new { googleIdToken = otherGoogleAccount }), HttpStatusCode.Forbidden);
        Assert.Equal("ReauthenticationFailed", code);
        Assert.Equal("حساب Google هذا غير مرتبط بحسابك في سرد", message);

        (code, _) = await Refusal(await DeleteAccount(member, new { googleIdToken = "not-a-google-token" }), HttpStatusCode.Forbidden);
        Assert.Equal("ReauthenticationFailed", code);

        // There is no password to check.
        (code, _) = await Refusal(await DeleteAccount(member, new { password = Password }), HttpStatusCode.Forbidden);
        Assert.Equal("ReauthenticationFailed", code);
        Assert.Null((await Row(member.Id)).DeletedAt);

        var response = await DeleteAccount(member, new { googleIdToken = api.GoogleTokens.Issue(email, subject: subject) });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull((await Row(member.Id)).DeletedAt);

        // Signing in with that Google account again makes a new, empty account: the deleted one never comes back.
        var again = await api.Client().PostAsJsonAsync("/api/identity/google-login", new { idToken = api.GoogleTokens.Issue(email, subject: subject) });
        var fresh = new ApiUser("", "", (await again.OkJson()).GetProperty("accessToken").GetString()!);
        var profile = await MyProfile(fresh);
        Assert.NotEqual(member.Id, profile.GetProperty("id").GetString());
        Assert.NotEqual(DeletedAccounts.DisplayName, profile.GetProperty("displayName").GetString());
        Assert.Equal(DeletedAccounts.DisplayName, (await Row(member.Id)).DisplayName);
    }

    [Fact]
    public async Task An_account_with_a_password_and_a_Google_sign_in_may_confirm_with_Google()
    {
        var member = await api.SignUp();
        var subject = "google-" + Guid.NewGuid().ToString("N");
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.AddLoginAsync((await users.FindByIdAsync(member.Id))!, new UserLoginInfo("Google", subject, "Google"))).Succeeded);
        }

        var response = await DeleteAccount(member, new { googleIdToken = api.GoogleTokens.Issue($"{member.UserName}@example.test", subject: subject) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull((await Row(member.Id)).DeletedAt);
    }

    [Fact]
    public async Task A_Google_account_may_confirm_with_a_sign_in_from_the_last_ten_minutes()
    {
        var (member, _, _) = await SignUpWithGoogle();

        var (code, message) = await Refusal(await DeleteAccount(WithTokenIssuedAt(member, DateTime.UtcNow.AddMinutes(-11))), HttpStatusCode.Forbidden);
        Assert.Equal("ReauthenticationRequired", code);
        Assert.Equal("لتأكيد حذف حسابك سجّل الدخول بحساب Google مرة أخرى، ثم أكّد الحذف خلال 10 دقائق", message);
        Assert.Null((await Row(member.Id)).DeletedAt);

        // The token of the sign-in just made, and no body at all (no content type either), as the web sends it.
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(member)).StatusCode);
        Assert.NotNull((await Row(member.Id)).DeletedAt);

        // Nine minutes is still recent.
        var (other, _, _) = await SignUpWithGoogle();
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(WithTokenIssuedAt(other, DateTime.UtcNow.AddMinutes(-9)))).StatusCode);
    }

    [Fact]
    public async Task Admins_cannot_delete_their_account_here()
    {
        var admin = await api.SignUpAdmin();

        var (code, message) = await Refusal(await DeleteAccount(admin, new { password = Password }), HttpStatusCode.Forbidden);

        Assert.Equal("AdminCannotDeleteAccount", code);
        Assert.Equal("لا يمكن حذف حساب مشرف من هنا، تواصل مع فريق سرد لإزالة صلاحياتك أولاً", message);
        Assert.Null((await Row(admin.Id)).DeletedAt);
        Assert.Equal(HttpStatusCode.OK, (await api.Get("/api/User/my-profile", admin)).StatusCode);
    }

    [Fact]
    public async Task Five_attempts_an_hour_then_429_even_with_the_right_password()
    {
        var member = await api.SignUp();
        for (var i = 0; i < AccountDeletionAttemptsLimit; i++)
        {
            Assert.Equal("ReauthenticationFailed",
                (await Refusal(await DeleteAccount(member, new { password = $"guess-{i}" }), HttpStatusCode.Forbidden)).Code);
        }

        var (code, message) = await Refusal(await DeleteAccount(member, new { password = Password }), HttpStatusCode.TooManyRequests);

        Assert.Equal("TooManyDeletionAttempts", code);
        Assert.Equal("حاولت حذف حسابك مرات كثيرة، حاول مرة أخرى بعد ساعة", message);
        Assert.Null((await Row(member.Id)).DeletedAt);

        // Per account: someone else is not held back.
        await DeleteWithPassword(await api.SignUp());
    }

    private const int AccountDeletionAttemptsLimit = Application.Users.Commands.DeleteAccount.AccountDeletionAttempts.Limit;

    [Fact]
    public async Task Signing_in_is_needed_and_a_double_tap_deletes_once()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Send(HttpMethod.Delete, "/api/User/me", null, JsonContent.Create(new { password = Password }))).StatusCode);

        var member = await api.SignUp();
        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IWalletService>().AddPointsAsync(member.Id, 50, TransactionType.GiftReceived, "هدية");
        }

        var responses = await Task.WhenAll(DeleteAccount(member, new { password = Password }), DeleteAccount(member, new { password = Password }));

        // The second waits for the first, then finds nothing left to do (or its token is refused already).
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.Unauthorized }));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        await using var db = api.Db();
        Assert.Single(await db.PointTransactions.Where(t => t.UserId == member.Id && t.Type == TransactionType.BalanceForfeited).ToListAsync());
    }

    // ─── What deletion does ───

    [Fact]
    public async Task The_account_is_anonymized_and_can_never_sign_in_again()
    {
        var member = await api.SignUp();
        var firstName = member.UserName;
        var email = $"{firstName}@example.test";
        var renamed = "r" + Guid.NewGuid().ToString("N")[..10];
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, "/api/User/update-me", member, ReaderApi.Form(
            ("UserName", renamed), ("UserBio", "نبذة عني"), ("FacebookUrl", "https://facebook.com/me"),
            ("TwitterUrl", "https://x.com/me"), ("DiscordUrl", "me#1234")))).StatusCode);
        // The first name is an old name now, and still finds the member.
        Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/User/{firstName}")).StatusCode);

        // A photo and a banner in storage, a phone number, a Google sign-in.
        var photoKey = $"profile-images/{member.Id}/{Guid.NewGuid():N}.png";
        var bannerKey = $"profile-banners/{member.Id}/{Guid.NewGuid():N}.png";
        var photo = await api.Storage.PutAsync(photoKey, new MemoryStream([1, 2, 3]), "image/png");
        var banner = await api.Storage.PutAsync(bannerKey, new MemoryStream([4, 5, 6]), "image/png");
        await using (var db = api.Db())
        {
            await db.Users.Where(u => u.Id == member.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.ProfilePhoto, photo)
                .SetProperty(u => u.ProfileBanner, banner)
                .SetProperty(u => u.PhoneNumber, "+201000000000")
                .SetProperty(u => u.PhoneNumberConfirmed, true));
        }
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByIdAsync(member.Id))!;
            Assert.True((await users.AddLoginAsync(user, new UserLoginInfo("Google", "google-" + Guid.NewGuid().ToString("N"), "Google"))).Succeeded);
        }
        var before = await Row(member.Id);

        await DeleteWithPassword(member);

        var row = await Row(member.Id);
        Assert.Matches("^deleted-[0-9a-f]{12}$", row.UserName);
        Assert.Equal(row.UserName!.ToUpperInvariant(), row.NormalizedUserName);
        Assert.Equal("مستخدم محذوف", row.DisplayName);
        Assert.Null(row.Email);
        Assert.Null(row.NormalizedEmail);
        Assert.False(row.EmailConfirmed);
        Assert.Null(row.PhoneNumber);
        Assert.False(row.PhoneNumberConfirmed);
        Assert.Null(row.UserBio);
        Assert.Null(row.FacebookUrl);
        Assert.Null(row.TwitterUrl);
        Assert.Null(row.DiscordUrl);
        Assert.Null(row.ProfilePhoto);
        Assert.Null(row.ProfileBanner);
        Assert.Null(row.PasswordHash);
        Assert.NotEqual(before.SecurityStamp, row.SecurityStamp);
        Assert.NotEqual(before.ConcurrencyStamp, row.ConcurrencyStamp);
        Assert.NotNull(row.TokensValidAfter);
        Assert.NotNull(row.DeletedAt);
        Assert.Equal(before.CreatedAt, row.CreatedAt);
        Assert.DoesNotContain(renamed, row.SearchName);
        Assert.DoesNotContain(firstName, row.SearchName);

        await using (var db = api.Db())
        {
            Assert.False(await db.UserLogins.AnyAsync(l => l.UserId == member.Id));
            // The old names lead nowhere now (neither the first name nor the one it had when deleted).
            Assert.False(await db.UserNameChanges.AnyAsync(c => c.UserId == member.Id));
        }
        Assert.False(api.Storage.Objects.ContainsKey(photoKey));
        Assert.False(api.Storage.Objects.ContainsKey(bannerKey));

        // Every session ended: the token is refused, a second deletion too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/User/my-profile", member)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAccount(member, new { password = Password })).StatusCode);

        // No sign-in by any name or the email, and no profile under any name it had.
        foreach (var login in new[] { firstName, renamed, email, row.UserName })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await api.Login(login)).StatusCode);
        }
        foreach (var name in new[] { firstName, renamed, row.UserName })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/User/{name}")).StatusCode);
        }

        // Not in search, by its old name or the deleted accounts' shared name.
        foreach (var query in new[] { renamed, row.UserName, DeletedAccounts.DisplayName })
        {
            var found = await (await api.Get($"/api/search/users?query={Uri.EscapeDataString(query)}")).OkJson();
            Assert.DoesNotContain(found.GetProperty("items").EnumerateArray(), u => u.GetProperty("id").GetString() == member.Id);
        }
    }

    [Fact]
    public async Task A_photo_that_is_not_in_our_storage_or_that_another_account_uses_is_left_alone()
    {
        var (member, other) = (await api.SignUp(), await api.SignUp());
        var sharedKey = $"profile-images/{Guid.NewGuid():N}";
        var shared = await api.Storage.PutAsync(sharedKey, new MemoryStream([7]), "image/png");
        await using (var db = api.Db())
        {
            await db.Users.Where(u => u.Id == member.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.ProfilePhoto, "https://lh3.googleusercontent.com/a/photo")
                .SetProperty(u => u.ProfileBanner, shared));
            await db.Users.Where(u => u.Id == other.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfilePhoto, shared));
        }

        await DeleteWithPassword(member);

        Assert.True(api.Storage.Objects.ContainsKey(sharedKey));
        var row = await Row(member.Id);
        Assert.Null(row.ProfilePhoto);
        Assert.Null(row.ProfileBanner);
    }

    [Fact]
    public async Task The_authors_novels_are_soft_deleted_and_disappear()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author, title: "رواية للحذف " + Seed.Marker());
        await api.AddChapter(novel, "<p>فقرة</p>");
        var draft = await api.AddNovel(author, isDraft: true);
        Assert.Equal(HttpStatusCode.OK, (await api.Get(ReaderApi.BySlug(novel.Slug))).StatusCode);
        Assert.Contains((await (await api.Get("/api/seo/sitemap")).OkJson()).EnumerateArray(), n => n.GetProperty("id").GetGuid() == novel.Id);

        await DeleteWithPassword(author);

        await using (var db = api.Db())
        {
            var novels = await db.Novels.IgnoreQueryFilters().AsNoTracking().Where(n => n.AuthorId == author.Id).ToListAsync();
            Assert.Equal(2, novels.Count);
            Assert.All(novels, n => Assert.True(n.IsDeleted && !n.IsEligibleForRanking));
            Assert.Contains(novels, n => n.Id == draft.Id);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get(ReaderApi.BySlug(novel.Slug))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/novel/by-id/{novel.Id}")).StatusCode);
        Assert.DoesNotContain((await (await api.Get("/api/seo/sitemap")).OkJson()).EnumerateArray(), n => n.GetProperty("id").GetGuid() == novel.Id);
        var search = await (await api.Get($"/api/search/novels?query={Uri.EscapeDataString(novel.Title)}")).OkJson();
        Assert.DoesNotContain(search.GetProperty("items").EnumerateArray(), n => n.GetProperty("id").GetGuid() == novel.Id);
    }

    [Fact]
    public async Task Private_data_goes_and_every_counter_matches_a_recount()
    {
        var (member, friend, fan, reviewer) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (blocked, blocker) = (await api.SignUp(), await api.SignUp());
        var memberName = member.UserName; // also the display name SignUp gives
        var novel = await api.AddNovel(friend);
        var (chapter, _) = await api.AddChapter(novel, "<p>فقرة</p>");

        // Library.
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/library/track-progress/{chapter.Id}", member)).StatusCode);
        Assert.Equal(1, (await Row(member.Id)).LibraryNovelsCount);

        // Reading lists: theirs (the fan follows it, and it holds a novel), and the friend's, which they follow.
        var ownList = await api.ReadingList(member);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/readinglist/{ownList}/novels/{novel.Id}", member)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/readinglist/{ownList}/follow", fan)).StatusCode);
        var friendsList = await api.ReadingList(friend);
        foreach (var follower in new[] { member, fan })
        {
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/readinglist/{friendsList}/follow", follower)).StatusCode);
        }

        // Follows both ways.
        Assert.Equal(HttpStatusCode.OK, (await api.Follow(member, friend)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Follow(fan, member)).StatusCode);

        // Likes on a comment, a review and a post; the fan likes them too, so each counter has something to keep.
        var comment = await api.Comment(friend, $"/api/comment/chapter/{chapter.Id}");
        var review = await api.Review(reviewer, novel.Id);
        var post = await api.Post(friend);
        foreach (var liker in new[] { member, fan })
        {
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/comment/{comment}/like", liker)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/{novel.Id}/reviews/{review}/like", liker)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/posts/{post}/like", liker)).StatusCode);
        }
        // A comment by the member on the friend's post: it stays, and its notification loses the member's name.
        await api.Comment(member, $"/api/comment/post/{post}");

        // Notifications to the member (the fan's follow) and from the member (follow, list follow, likes, comment).
        await api.WaitForNotificationsFrom(member, fan);
        await api.WaitForNotificationsFrom(friend, member, count: 5);
        await api.WaitForNotificationsFrom(reviewer, member); // the review like

        // A privilege subscription, a phone for pushes with preferences, and blocks both ways.
        await using (var db = api.Db())
        {
            db.NovelPrivilegeSubscriptions.Add(new NovelPrivilegeSubscription
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, UserId = member.Id, AmountPaid = 100, SubscribedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Post, "/api/notifications/devices", member,
            JsonContent.Create(new { token = "fcm-" + Guid.NewGuid().ToString("N"), platform = "android" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, "/api/notifications/preferences", member,
            JsonContent.Create(new { social = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Block(member, blocked)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Block(blocker, member)).StatusCode);

        await DeleteWithPassword(member);

        await using (var db = api.Db())
        {
            Assert.False(await db.UserNovelProgress.AnyAsync(p => p.UserId == member.Id));
            Assert.Equal(0, (await Row(member.Id)).LibraryNovelsCount);
            Assert.False(await db.ReadingLists.AnyAsync(l => l.UserId == member.Id));
            Assert.False(await db.ReadingListNovels.AnyAsync(n => n.ReadingListId == ownList));
            Assert.False(await db.ReadingListFollowers.AnyAsync(f => f.UserId == member.Id || f.ReadingListId == ownList));
            Assert.False(await db.Follows.AnyAsync(f => f.FollowerId == member.Id || f.FollowedId == member.Id));
            Assert.False(await db.Notifications.AnyAsync(n => n.UserId == member.Id));
            Assert.False(await db.CommentLikes.AnyAsync(l => l.UserId == member.Id));
            Assert.False(await db.ReviewLikes.AnyAsync(l => l.UserId == member.Id));
            Assert.False(await db.PostLikes.AnyAsync(l => l.UserId == member.Id));
            Assert.False(await db.NovelPrivilegeSubscriptions.AnyAsync(s => s.UserId == member.Id));
            Assert.False(await db.UserDevices.AnyAsync(d => d.UserId == member.Id));
            Assert.False(await db.NotificationPreferences.AnyAsync(p => p.UserId == member.Id));
            Assert.False(await db.UserBlocks.AnyAsync(b => b.BlockerId == member.Id || b.BlockedId == member.Id));

            // Counters are what a recount gives: the fan's like, the fan's follow.
            var likedComment = await db.Comments.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == comment);
            Assert.Equal(await db.CommentLikes.CountAsync(l => l.CommentId == comment), likedComment.LikesCount);
            Assert.Equal(1, likedComment.LikesCount);
            var likedReview = await db.Reviews.AsNoTracking().SingleAsync(r => r.Id == review);
            Assert.Equal(await db.ReviewLikes.CountAsync(l => l.ReviewId == review), likedReview.LikeCount);
            Assert.Equal(1, likedReview.LikeCount);
            var likedPost = await db.Posts.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == post);
            Assert.Equal(await db.PostLikes.CountAsync(l => l.PostId == post), likedPost.LikesCount);
            Assert.Equal(1, likedPost.LikesCount);
            var followedList = await db.ReadingLists.AsNoTracking().SingleAsync(l => l.Id == friendsList);
            Assert.Equal(await db.ReadingListFollowers.CountAsync(f => f.ReadingListId == friendsList), followedList.FollowersCount);
            Assert.Equal(1, followedList.FollowersCount);

            // What they caused others stays, without their name or photo.
            var caused = await db.Notifications.AsNoTracking().Where(n => n.ActorId == member.Id).ToListAsync();
            Assert.True(caused.Count >= 6, $"{caused.Count} notifications from the member");
            Assert.All(caused, n =>
            {
                Assert.Equal(DeletedAccounts.DisplayName, n.ActorDisplayName);
                Assert.Null(n.ActorProfilePhoto);
                Assert.StartsWith(DeletedAccounts.DisplayName + " ", n.Message);
                Assert.DoesNotContain(memberName, n.Message);
            });

            // Their comment stays, under the deleted account.
            Assert.True(await db.Comments.AnyAsync(c => c.UserId == member.Id && c.PostId == post));
        }

        // Follower totals are counts of what's left.
        Assert.Equal(0, (await (await api.Get($"/api/User/{friend.UserName}")).OkJson()).GetProperty("totalFollowers").GetInt32());
        Assert.Equal(0, (await (await api.Get($"/api/User/{fan.UserName}")).OkJson()).GetProperty("totalFollowing").GetInt32());
    }

    [Fact]
    public async Task The_balance_is_forfeited_with_a_ledger_row_and_pending_withdrawals_are_cancelled()
    {
        var (saver, debtor) = (await api.SignUp(), await api.SignUp());
        var token = "play-token-" + Guid.NewGuid().ToString("N");
        Guid pending, approved;
        using (var scope = api.Services.CreateScope())
        {
            var wallet = scope.ServiceProvider.GetRequiredService<IWalletService>();
            await wallet.AddPointsAsync(saver.Id, 500, TransactionType.RechargeApproved, "شحن");
            await wallet.AddPointsAsync(debtor.Id, 100, TransactionType.PlayPurchase, "Google Play");
            // Google refunded that purchase after the points were spent elsewhere: the balance went below zero.
            await scope.ServiceProvider.GetRequiredService<IUserWalletRepository>().DebitAllowingNegativeAsync(debtor.Id, 220);
        }
        await using (var db = api.Db())
        {
            db.PointTransactions.Add(new PointTransaction
            {
                Id = Guid.NewGuid(), UserId = debtor.Id, Type = TransactionType.PlayRefund, Amount = -220, BalanceBefore = 100,
                BalanceAfter = -120, Description = "Google Play refund", CreatedAt = DateTime.UtcNow
            });
            db.PlayPurchases.Add(new PlayPurchase
            {
                Id = Guid.NewGuid(), PurchaseToken = token, UserId = debtor.Id, ProductId = "points_100", Points = 100,
                Status = PlayPurchaseStatus.Voided, CreatedAt = DateTime.UtcNow
            });
            pending = Guid.NewGuid();
            approved = Guid.NewGuid();
            db.WithdrawalRequests.AddRange(
                Withdrawal(pending, saver.Id, RequestStatus.Pending),
                Withdrawal(approved, saver.Id, RequestStatus.Approved));
            await db.SaveChangesAsync();
        }

        await DeleteWithPassword(saver);
        await DeleteWithPassword(debtor);

        await using (var db = api.Db())
        {
            foreach (var (user, balance) in new[] { (saver, 500m), (debtor, -120m) })
            {
                Assert.Equal(0, (await db.UserWallets.AsNoTracking().SingleAsync(w => w.UserId == user.Id)).CurrentBalance);
                Assert.Equal(0, (await Row(user.Id)).PointBalance);

                var ledger = await db.PointTransactions.AsNoTracking().Where(t => t.UserId == user.Id).OrderBy(t => t.CreatedAt).ToListAsync();
                var forfeit = Assert.Single(ledger, t => t.Type == TransactionType.BalanceForfeited);
                Assert.Equal(-balance, forfeit.Amount);
                Assert.Equal(balance, forfeit.BalanceBefore);
                Assert.Equal(0, forfeit.BalanceAfter);
                Assert.Equal(AccountDeletionService.ForfeitDescription, forfeit.Description);
                // The earlier rows are kept, and the ledger still adds up to the balance.
                Assert.True(ledger.Count > 1);
                Assert.Equal(0, ledger.Sum(t => t.Amount));
            }

            Assert.True(await db.PlayPurchases.AnyAsync(p => p.PurchaseToken == token && p.UserId == debtor.Id));

            var cancelled = await db.WithdrawalRequests.AsNoTracking().SingleAsync(r => r.Id == pending);
            Assert.Equal(RequestStatus.Rejected, cancelled.Status);
            Assert.Equal(DeletedAccounts.WithdrawalCancelledReason, cancelled.RejectionReason);
            Assert.NotNull(cancelled.ProcessedAt);
            Assert.Null(cancelled.ProcessedBy);
            Assert.Equal(RequestStatus.Approved, (await db.WithdrawalRequests.AsNoTracking().SingleAsync(r => r.Id == approved)).Status);
        }
    }

    private static WithdrawalRequest Withdrawal(Guid id, string userId, string status) => new()
    {
        Id = id, UserId = userId, PointsRequested = 200, BaseAmountEGP = 20, TaxDeducted = 2, NetAmountEGP = 18,
        WithdrawalMethod = PaymentMethod.VodafoneCash, PaymentDetails = "01000000000", Status = status,
        RequestedAt = DateTime.UtcNow, ProcessedAt = status == RequestStatus.Pending ? null : DateTime.UtcNow
    };

    [Fact]
    public async Task Open_reports_about_them_close_and_the_reports_they_made_stay_open()
    {
        var admin = await api.SignUpAdmin();
        var (member, reporter, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (memberPost, dismissedPost) = (await api.Post(member), await api.Post(member));
        var aboutUser = await api.Reported(reporter, "User", member.Id);
        var aboutPost = await api.Reported(other, "Post", memberPost);
        var dismissed = await api.Reported(reporter, "Post", dismissedPost);
        Assert.Equal(HttpStatusCode.OK, (await api.Resolve(admin, dismissed, "Dismiss")).StatusCode);
        var theirs = await api.Reported(member, "Post", await api.Post(other));

        await DeleteWithPassword(member);

        await using (var db = api.Db())
        {
            foreach (var id in new[] { aboutUser, aboutPost })
            {
                var closed = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == id);
                Assert.Equal(ReportStatus.Resolved, closed.Status);
                Assert.Equal(ReportAction.AccountDeleted, closed.Resolution);
                Assert.NotNull(closed.ResolvedAt);
                Assert.Null(closed.ResolvedById);
            }
            Assert.Equal(ReportStatus.Dismissed, (await db.Reports.AsNoTracking().SingleAsync(r => r.Id == dismissed)).Status);
            Assert.Equal(ReportStatus.Open, (await db.Reports.AsNoTracking().SingleAsync(r => r.Id == theirs)).Status);
        }

        // The moderators see why they closed.
        var list = await api.AdminReports(admin, "Resolved");
        var item = list.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == aboutUser);
        Assert.Equal("AccountDeleted", item.GetProperty("action").GetString());
        Assert.True(item.GetProperty("target").GetProperty("isDeleted").GetBoolean());

        // It isn't an action admins can take, and a deleted account can't be reported.
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Resolve(admin, theirs, "AccountDeleted")).StatusCode);
        Assert.Equal("TargetNotFound", (await (await api.Report(other, "User", member.Id)).Error(HttpStatusCode.NotFound)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deleted_accounts_are_left_out_of_supporters_and_follower_lists()
    {
        var (member, author, fan) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        await using (var db = api.Db())
        {
            var gift = await db.Gifts.AsNoTracking().OrderBy(g => g.Cost).FirstAsync();
            db.GiftTransactions.AddRange(
                new GiftTransaction { Id = Guid.NewGuid(), GiftId = gift.Id, NovelId = novel.Id, SenderId = member.Id, Count = 10, TotalCost = 100_000, CreatedAt = DateTime.UtcNow },
                new GiftTransaction { Id = Guid.NewGuid(), GiftId = gift.Id, NovelId = novel.Id, SenderId = fan.Id, Count = 1, TotalCost = 99_000, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        async Task Recalculate()
        {
            using var scope = api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IGlobalSupporterLeaderboardRepository>().RecalculateAllTimeLeaderboard();
        }
        async Task<List<JsonElement>> Board() =>
            (await (await api.Get("/api/gift/leaderboard/AllTime?pageSize=100")).OkJson()).GetProperty("supporters").EnumerateArray().ToList();
        await Recalculate();
        var ranked = await Board();
        var memberRank = ranked.Single(s => s.GetProperty("userId").GetString() == member.Id).GetProperty("rank").GetInt32();
        var fanRank = ranked.Single(s => s.GetProperty("userId").GetString() == fan.Id).GetProperty("rank").GetInt32();
        Assert.Equal(memberRank + 1, fanRank);

        await DeleteWithPassword(member);

        // Off the board at once, the members below moving up; and a recalculation leaves them out too.
        foreach (var recalculated in new[] { false, true })
        {
            if (recalculated) await Recalculate();
            var board = await Board();
            Assert.DoesNotContain(board, s => s.GetProperty("userId").GetString() == member.Id);
            Assert.Equal(memberRank, board.Single(s => s.GetProperty("userId").GetString() == fan.Id).GetProperty("rank").GetInt32());
            Assert.Equal(Enumerable.Range(1, board.Count), board.Select(s => s.GetProperty("rank").GetInt32()).Order());
        }
        var top = await (await api.Get($"/api/gift/novel/{novel.Id}/top-supporters")).OkJson();
        Assert.Equal(new[] { fan.Id }, top.EnumerateArray().Select(s => s.GetProperty("userId").GetString()!));

        // A follow that raced the deletion isn't listed or counted.
        await using (var db = api.Db())
        {
            db.Follows.Add(new Follow { FollowerId = member.Id, FollowedId = author.Id, CreatedAt = DateTime.UtcNow });
            db.Follows.Add(new Follow { FollowerId = author.Id, FollowedId = member.Id, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var followers = await (await api.Get($"/api/User/followers-list/{author.Id}")).OkJson();
        Assert.Equal(0, followers.GetProperty("totalItemsCount").GetInt32());
        var following = await (await api.Get($"/api/User/following-list/{author.Id}")).OkJson();
        Assert.Equal(0, following.GetProperty("totalItemsCount").GetInt32());
        var profile = await (await api.Get($"/api/User/{author.UserName}")).OkJson();
        Assert.Equal(0, profile.GetProperty("totalFollowers").GetInt32());
        Assert.Equal(0, profile.GetProperty("totalFollowing").GetInt32());
    }
}
