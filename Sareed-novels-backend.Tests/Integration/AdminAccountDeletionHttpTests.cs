using System.Net;
using System.Net.Http.Json;
using System.Text;
using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Moderation;
using Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// DELETE /api/admin/users/{userId} through the real pipeline: only admins, never an admin's account, the body's
/// validation, the audit row, and a member left exactly as their own deletion (DELETE /api/User/me) leaves them.
/// </summary>
public class AdminAccountDeletionHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private const string Password = ModerationApi.Password;

    private Task<HttpResponseMessage> AdminDelete(ApiUser? caller, string userId, object? body) =>
        api.Send(HttpMethod.Delete, $"/api/admin/users/{userId}", caller, body is null ? null : JsonContent.Create(body));

    private Task<HttpResponseMessage> AdminDeleteRaw(ApiUser caller, string userId, string? json) =>
        api.Send(HttpMethod.Delete, $"/api/admin/users/{userId}", caller,
            json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<User> Row(string userId)
    {
        await using var db = api.Db();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    private async Task<List<AdminAuditLog>> AuditRows(string userId)
    {
        await using var db = api.Db();
        return await db.AdminAuditLogs.AsNoTracking().Where(a => a.TargetUserId == userId).ToListAsync();
    }

    private static async Task<(string Code, string Message)> Refusal(HttpResponseMessage response, HttpStatusCode status)
    {
        var body = await response.Error(status);
        return (body.GetProperty("code").GetString()!, body.GetProperty("message").GetString()!);
    }

    /// <summary>The account still works: not deleted, its token accepted, and nothing recorded about it.</summary>
    private async Task AssertUntouched(ApiUser user)
    {
        Assert.Null((await Row(user.Id)).DeletedAt);
        Assert.Equal(HttpStatusCode.OK, (await api.Get("/api/User/my-profile", user)).StatusCode);
        Assert.Empty(await AuditRows(user.Id));
    }

    // ─── Who may, and whom ───

    [Fact]
    public async Task Only_admins_may_delete_an_account()
    {
        var (member, other) = (await api.SignUp(), await api.SignUp());

        Assert.Equal(HttpStatusCode.Unauthorized, (await AdminDelete(null, member.Id, new { reason = "Underage" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminDelete(other, member.Id, new { reason = "Underage" })).StatusCode);
        // Not even their own, here: members delete theirs with DELETE /api/User/me.
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminDelete(member, member.Id, new { reason = "OwnerRequest" })).StatusCode);

        await AssertUntouched(member);
        await AssertUntouched(other);
    }

    [Fact]
    public async Task An_admins_account_is_refused_the_callers_own_too()
    {
        var (admin, otherAdmin) = (await api.SignUpAdmin(), await api.SignUpAdmin());

        foreach (var target in new[] { otherAdmin, admin })
        {
            var (code, message) = await Refusal(await AdminDelete(admin, target.Id, new { reason = "PolicyViolation" }), HttpStatusCode.Forbidden);

            Assert.Equal("CannotDeleteAdmin", code);
            Assert.Equal("لا يمكن حذف حساب مشرف، أزل صلاحية الإشراف عنه أولاً", message);
        }
        await AssertUntouched(otherAdmin);
        await AssertUntouched(admin);
    }

    [Fact]
    public async Task An_unknown_user_is_a_404_and_a_deleted_account_a_409()
    {
        var admin = await api.SignUpAdmin();

        foreach (var unknown in new[] { Guid.NewGuid().ToString(), "not-a-user-id" })
        {
            var (code, message) = await Refusal(await AdminDelete(admin, unknown, new { reason = "Underage" }), HttpStatusCode.NotFound);
            Assert.Equal("UserNotFound", code);
            Assert.Equal("المستخدم غير موجود", message);
        }

        // Deleted by an admin, then again (a retry after a lost response): done already.
        var member = await api.SignUp();
        await (await AdminDelete(admin, member.Id, new { reason = "Underage" })).OkJson();
        var (again, againMessage) = await Refusal(await AdminDelete(admin, member.Id, new { reason = "Underage" }), HttpStatusCode.Conflict);
        Assert.Equal("AlreadyDeleted", again);
        Assert.Equal("هذا الحساب محذوف بالفعل", againMessage);
        Assert.Single(await AuditRows(member.Id));

        // Deleted by the member themselves.
        var selfDeleted = await api.SignUp();
        Assert.Equal(HttpStatusCode.NoContent,
            (await api.Send(HttpMethod.Delete, "/api/User/me", selfDeleted, JsonContent.Create(new { password = Password }))).StatusCode);
        var (code409, _) = await Refusal(await AdminDelete(admin, selfDeleted.Id, new { reason = "OwnerRequest" }), HttpStatusCode.Conflict);
        Assert.Equal("AlreadyDeleted", code409);
        Assert.Empty(await AuditRows(selfDeleted.Id));
    }

    [Fact]
    public async Task Two_admins_at_once_delete_the_account_once_and_record_it_once()
    {
        var (first, second) = (await api.SignUpAdmin(), await api.SignUpAdmin());
        var member = await api.SignUp();
        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IWalletService>().AddPointsAsync(member.Id, 50, TransactionType.GiftReceived, "هدية");
        }

        var responses = await Task.WhenAll(
            AdminDelete(first, member.Id, new { reason = "PolicyViolation" }),
            AdminDelete(second, member.Id, new { reason = "PolicyViolation" }));

        // The second waits for the first, then finds it done.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        Assert.Single(await AuditRows(member.Id));
        await using var db = api.Db();
        Assert.Single(await db.PointTransactions.Where(t => t.UserId == member.Id && t.Type == TransactionType.BalanceForfeited).ToListAsync());
    }

    // ─── The body ───

    [Theory]
    [InlineData(null, ValidationMessages.Reason)]
    [InlineData("null", ValidationMessages.Reason)]
    [InlineData("{}", ValidationMessages.Reason)]
    [InlineData("{\"reason\":\"\"}", ValidationMessages.Reason)]
    [InlineData("{\"reason\":\"Spam\"}", ValidationMessages.Reason)]
    [InlineData("{\"reason\":\"1\"}", ValidationMessages.Reason)]
    [InlineData("{\"reason\":1}", ValidationMessages.UnreadableBody)]
    [InlineData("{\"reason\":\"Underage\",\"note\":\"LONG\"}", ValidationMessages.Note)]
    public async Task A_missing_or_unknown_reason_and_a_note_over_500_characters_are_a_400(string? json, string message)
    {
        var admin = await api.SignUpAdmin();
        var member = await api.SignUp();

        var response = await AdminDeleteRaw(admin, member.Id, json?.Replace("LONG", new string('ن', AdminAuditLog.NoteMaxLength + 1)));

        var (code, answered) = await Refusal(response, HttpStatusCode.BadRequest);
        Assert.Equal(Sareed_novels_backend.Middlewares.ValidationProblems.Code, code);
        Assert.Equal(message, answered);
        await AssertUntouched(member);
    }

    private static class ValidationMessages
    {
        public const string Reason = "سبب الحذف غير صالح: Underage أو PolicyViolation أو OwnerRequest";
        public const string Note = "يجب ألا تتجاوز الملاحظة 500 حرف";
        public const string UnreadableBody = Sareed_novels_backend.Middlewares.ValidationProblems.UnreadableBodyMessage;
    }

    // ─── What it does ───

    [Fact]
    public async Task The_deletion_is_recorded_with_the_admin_the_target_and_the_reason_and_answers_what_it_did()
    {
        var admin = await api.SignUpAdmin();
        var member = await api.SignUp();
        var novel = await api.AddNovel(member);
        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IWalletService>().AddPointsAsync(member.Id, 300, TransactionType.RechargeApproved, "شحن");
        }

        // The id as the admin typed it, in capitals: the record keeps the account's own.
        var result = await (await AdminDelete(admin, member.Id.ToUpperInvariant(),
            new { reason = "ownerRequest", note = "  طلب الحذف من بريده المسجّل  " })).OkJson();

        var row = await Row(member.Id);
        Assert.NotNull(row.DeletedAt);
        Assert.Equal(member.Id, result.GetProperty("userId").GetString());
        Assert.Equal("OwnerRequest", result.GetProperty("reason").GetString());
        Assert.EndsWith("Z", result.GetProperty("deletedAt").GetString());
        Assert.Equal(row.DeletedAt, result.GetProperty("deletedAt").GetDateTime().ToUniversalTime());
        Assert.Equal(1, result.GetProperty("novelsHidden").GetInt32());
        Assert.Equal(300, result.GetProperty("forfeitedBalance").GetDecimal());
        Assert.Equal(0, result.GetProperty("withdrawalsCancelled").GetInt32());
        Assert.Equal(0, result.GetProperty("reportsClosed").GetInt32());
        Assert.Equal(0, result.GetProperty("filesNotDeleted").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get(ReaderApi.BySlug(novel.Slug))).StatusCode);

        var audit = Assert.Single(await AuditRows(member.Id));
        Assert.Equal(admin.Id, audit.AdminId);
        Assert.Equal(AdminAuditAction.DeleteAccount, audit.Action);
        Assert.Equal(member.Id, audit.TargetUserId);
        Assert.Equal("OwnerRequest", audit.Reason);
        Assert.Equal("طلب الحذف من بريده المسجّل", audit.Note);
        Assert.Equal(row.DeletedAt, audit.CreatedAt);
        Assert.NotEqual(Guid.Empty, audit.Id);
    }

    [Fact]
    public async Task An_admin_deletion_leaves_the_member_exactly_as_their_own_deletion_does()
    {
        var admin = await api.SignUpAdmin();
        var selfDeleted = await SeedFootprint();
        var adminDeleted = await SeedFootprint();
        await RecalculateSupporters();
        await using (var db = api.Db())
        {
            Assert.True(await db.GlobalSupporterLeaderboards.AnyAsync(l => l.UserId == selfDeleted.Member.Id));
            Assert.True(await db.GlobalSupporterLeaderboards.AnyAsync(l => l.UserId == adminDeleted.Member.Id));
        }

        Assert.Equal(HttpStatusCode.NoContent,
            (await api.Send(HttpMethod.Delete, "/api/User/me", selfDeleted.Member, JsonContent.Create(new { password = Password }))).StatusCode);
        var result = await (await AdminDelete(admin, adminDeleted.Member.Id, new { reason = "Underage" })).OkJson();

        var expected = await Remains(selfDeleted);
        var actual = await Remains(adminDeleted);
        Assert.Equal(expected, actual);

        // A deletion indeed, not two members left alike.
        Assert.Contains("deleted: True", actual);
        Assert.Contains($"display name: {DeletedAccounts.DisplayName}", actual);
        Assert.Contains("photo in storage: False", actual);
        Assert.Contains("wallet balance: 0.00", actual);
        Assert.Contains("devices: 0", actual);
        Assert.Contains("token: 401", actual);

        // What the admin is told matches.
        Assert.Equal(2, result.GetProperty("novelsHidden").GetInt32());
        Assert.Equal(500, result.GetProperty("forfeitedBalance").GetDecimal());
        Assert.Equal(1, result.GetProperty("withdrawalsCancelled").GetInt32());
        Assert.Equal(2, result.GetProperty("reportsClosed").GetInt32());
        Assert.Equal(2, result.GetProperty("filesDeleted").GetInt32());
        Assert.Equal(0, result.GetProperty("filesNotDeleted").GetInt32());

        // Only the admin's deletion is on record.
        Assert.Equal("Underage", Assert.Single(await AuditRows(adminDeleted.Member.Id)).Reason);
        Assert.Empty(await AuditRows(selfDeleted.Member.Id));
    }

    /// <summary>A member with something in every table a deletion touches, and the ids to find it all again.</summary>
    private sealed record Footprint(
        ApiUser Member, string FirstName, string Renamed, string PhotoKey, string BannerKey, User Before,
        ApiUser Friend, ApiUser Fan, string OwnNovelSlug, Guid OwnList, Guid FriendsList, Guid LikedComment, Guid LikedReview,
        Guid LikedPost, Guid OwnComment, Guid OwnPost, Guid PendingWithdrawal, Guid ApprovedWithdrawal, string PlayToken,
        Guid ReportAboutThem, Guid ReportAboutTheirPost, Guid ReportTheyMade);

    /// <summary>Seeds the same footprint for a new member, the way AccountDeletionHttpTests seeds each part of it.</summary>
    private async Task<Footprint> SeedFootprint()
    {
        var member = await api.SignUp();
        var (friend, fan, reviewer, blocked, blocker, reporter) =
            (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());

        // Profile: a new user name (the first becomes an old one), a bio and links; a phone, a photo and a banner in
        // storage; a Google sign-in.
        var renamed = "r" + Guid.NewGuid().ToString("N")[..10];
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, "/api/User/update-me", member, ReaderApi.Form(
            ("UserName", renamed), ("UserBio", "نبذة عني"), ("FacebookUrl", "https://facebook.com/me"),
            ("TwitterUrl", "https://x.com/me"), ("DiscordUrl", "me#1234")))).StatusCode);
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
            Assert.True((await users.AddLoginAsync((await users.FindByIdAsync(member.Id))!,
                new UserLoginInfo("Google", "google-" + Guid.NewGuid().ToString("N"), "Google"))).Succeeded);
        }

        // Their novels, a published one and a draft; a chapter of the friend's novel in their library.
        var ownNovel = await api.AddNovel(member);
        await api.AddNovel(member, isDraft: true);
        var novel = await api.AddNovel(friend);
        var (chapter, _) = await api.AddChapter(novel, "<p>فقرة</p>");
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/library/track-progress/{chapter.Id}", member)).StatusCode);

        // Reading lists: theirs (holding a novel, followed by the fan), and the friend's, which they and the fan follow.
        var ownList = await api.ReadingList(member);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/readinglist/{ownList}/novels/{novel.Id}", member)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/readinglist/{ownList}/follow", fan)).StatusCode);
        var friendsList = await api.ReadingList(friend);
        foreach (var follower in new[] { member, fan })
        {
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/readinglist/{friendsList}/follow", follower)).StatusCode);
        }

        // Follows both ways; likes on a comment, a review and a post that the fan likes too; a comment and a post of their own.
        Assert.Equal(HttpStatusCode.OK, (await api.Follow(member, friend)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Follow(fan, member)).StatusCode);
        var comment = await api.Comment(friend, $"/api/comment/chapter/{chapter.Id}");
        var review = await api.Review(reviewer, novel.Id);
        var post = await api.Post(friend);
        foreach (var liker in new[] { member, fan })
        {
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/comment/{comment}/like", liker)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/{novel.Id}/reviews/{review}/like", liker)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/posts/{post}/like", liker)).StatusCode);
        }
        var ownComment = await api.Comment(member, $"/api/comment/post/{post}");
        var ownPost = await api.Post(member);

        // Every notification those made (the fan's follow of them and of their list; their follow, list follow, likes
        // and comment), before anything is deleted.
        await api.WaitForNotificationsFrom(member, fan, count: 2);
        await api.WaitForNotificationsFrom(friend, member, count: 5);
        await api.WaitForNotificationsFrom(reviewer, member);

        // A phone for pushes with preferences, and blocks both ways.
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Post, "/api/notifications/devices", member,
            JsonContent.Create(new { token = "fcm-" + Guid.NewGuid().ToString("N"), platform = "android" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, "/api/notifications/preferences", member,
            JsonContent.Create(new { social = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Block(member, blocked)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Block(blocker, member)).StatusCode);

        // Money: 500 points, a pending and an approved withdrawal, a Google Play purchase; a privilege subscription and a
        // gift on the friend's novel.
        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IWalletService>().AddPointsAsync(member.Id, 500, TransactionType.RechargeApproved, "شحن");
        }
        var (pending, approved, playToken) = (Guid.NewGuid(), Guid.NewGuid(), "play-token-" + Guid.NewGuid().ToString("N"));
        await using (var db = api.Db())
        {
            db.WithdrawalRequests.AddRange(Withdrawal(pending, member.Id, RequestStatus.Pending), Withdrawal(approved, member.Id, RequestStatus.Approved));
            db.PlayPurchases.Add(new PlayPurchase
            {
                Id = Guid.NewGuid(), PurchaseToken = playToken, UserId = member.Id, ProductId = "points_100", Points = 100,
                Status = PlayPurchaseStatus.Credited, CreatedAt = DateTime.UtcNow
            });
            db.NovelPrivilegeSubscriptions.Add(new NovelPrivilegeSubscription
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, UserId = member.Id, AmountPaid = 100, SubscribedAt = DateTime.UtcNow
            });
            var gift = await db.Gifts.AsNoTracking().OrderBy(g => g.Cost).FirstAsync();
            db.GiftTransactions.Add(new GiftTransaction
            {
                Id = Guid.NewGuid(), GiftId = gift.Id, NovelId = novel.Id, SenderId = member.Id, Count = 1, TotalCost = gift.Cost, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Reports about them and their post, which close, and one they made, which stays open.
        var aboutThem = await api.Reported(reporter, "User", member.Id);
        var aboutTheirPost = await api.Reported(reporter, "Post", ownPost);
        var theyMade = await api.Reported(member, "Post", post);

        return new Footprint(member, member.UserName, renamed, photoKey, bannerKey, await Row(member.Id), friend, fan,
            ownNovel.Slug, ownList, friendsList, comment, review, post, ownComment, ownPost, pending, approved, playToken,
            aboutThem, aboutTheirPost, theyMade);
    }

    private static WithdrawalRequest Withdrawal(Guid id, string userId, string status) => new()
    {
        Id = id, UserId = userId, PointsRequested = 200, BaseAmountEGP = 20, TaxDeducted = 2, NetAmountEGP = 18,
        WithdrawalMethod = PaymentMethod.VodafoneCash, PaymentDetails = "01000000000", Status = status,
        RequestedAt = DateTime.UtcNow, ProcessedAt = status == RequestStatus.Pending ? null : DateTime.UtcNow
    };

    private async Task RecalculateSupporters()
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IGlobalSupporterLeaderboardRepository>().RecalculateAllTimeLeaderboard();
    }

    /// <summary>
    /// What is left of a footprint after a deletion, as facts that don't depend on the member's ids or names, so two
    /// members' remains compare equal exactly when the deletions did the same.
    /// </summary>
    private async Task<List<string>> Remains(Footprint f)
    {
        var facts = new List<string>();
        void Fact(string name, object? value) => facts.Add($"{name}: {value}");
        var id = f.Member.Id;
        await using var db = api.Db();
        var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);

        // The user row.
        var compactId = new string(id.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        Fact("user name is deleted-<id>", row.UserName == DeletedAccounts.UserNamePrefix + compactId[..12]);
        Fact("normalized user name matches", row.NormalizedUserName == row.UserName!.ToUpperInvariant());
        Fact("display name", row.DisplayName);
        Fact("email", row.Email);
        Fact("normalized email", row.NormalizedEmail);
        Fact("email confirmed", row.EmailConfirmed);
        Fact("phone", row.PhoneNumber);
        Fact("phone confirmed", row.PhoneNumberConfirmed);
        Fact("bio", row.UserBio);
        Fact("links", $"{row.FacebookUrl}|{row.TwitterUrl}|{row.DiscordUrl}");
        Fact("photo", row.ProfilePhoto);
        Fact("banner", row.ProfileBanner);
        Fact("has password", row.PasswordHash != null);
        Fact("two-factor", row.TwoFactorEnabled);
        Fact("lockout", $"{row.LockoutEnd}|{row.AccessFailedCount}");
        Fact("security stamp changed", row.SecurityStamp != f.Before.SecurityStamp);
        Fact("concurrency stamp changed", row.ConcurrencyStamp != f.Before.ConcurrencyStamp);
        Fact("tokens cut off", row.TokensValidAfter != null);
        Fact("deleted", row.DeletedAt != null);
        Fact("suspended", row.SuspendedUntil);
        Fact("created at kept", row.CreatedAt == f.Before.CreatedAt);
        Fact("search name has an old name", row.SearchName.Contains(f.FirstName) || row.SearchName.Contains(f.Renamed));
        Fact("library count", row.LibraryNovelsCount);
        Fact("point balance", row.PointBalance);
        Fact("roles", string.Join(",", await db.UserRoles.Where(r => r.UserId == id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name).OrderBy(n => n).ToListAsync()));

        // Their rows, table by table.
        Fact("external sign-ins", await db.UserLogins.CountAsync(l => l.UserId == id));
        Fact("identity tokens", await db.UserTokens.CountAsync(t => t.UserId == id));
        Fact("claims", await db.UserClaims.CountAsync(c => c.UserId == id));
        Fact("old user names", await db.UserNameChanges.CountAsync(c => c.UserId == id));
        Fact("reading progress", await db.UserNovelProgress.CountAsync(p => p.UserId == id));
        Fact("reading lists", await db.ReadingLists.CountAsync(l => l.UserId == id));
        Fact("their list's novels", await db.ReadingListNovels.CountAsync(n => n.ReadingListId == f.OwnList));
        Fact("list follows", await db.ReadingListFollowers.CountAsync(x => x.UserId == id || x.ReadingListId == f.OwnList));
        Fact("follows", await db.Follows.CountAsync(x => x.FollowerId == id || x.FollowedId == id));
        Fact("notifications to them", await db.Notifications.CountAsync(n => n.UserId == id));
        Fact("comment likes", await db.CommentLikes.CountAsync(l => l.UserId == id));
        Fact("review likes", await db.ReviewLikes.CountAsync(l => l.UserId == id));
        Fact("post likes", await db.PostLikes.CountAsync(l => l.UserId == id));
        Fact("privilege subscriptions", await db.NovelPrivilegeSubscriptions.CountAsync(s => s.UserId == id));
        Fact("devices", await db.UserDevices.CountAsync(d => d.UserId == id));
        Fact("notification preferences", await db.NotificationPreferences.CountAsync(p => p.UserId == id));
        Fact("blocks", await db.UserBlocks.CountAsync(b => b.BlockerId == id || b.BlockedId == id));
        Fact("supporters board", await db.GlobalSupporterLeaderboards.CountAsync(l => l.UserId == id));
        Fact("gifts sent (kept)", await db.GiftTransactions.CountAsync(g => g.SenderId == id));

        // The counters of what they liked or followed, and what a recount gives.
        var comment = await db.Comments.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == f.LikedComment);
        Fact("liked comment's likes", $"{comment.LikesCount} = {await db.CommentLikes.CountAsync(l => l.CommentId == f.LikedComment)}");
        var review = await db.Reviews.AsNoTracking().SingleAsync(r => r.Id == f.LikedReview);
        Fact("liked review's likes", $"{review.LikeCount} = {await db.ReviewLikes.CountAsync(l => l.ReviewId == f.LikedReview)}");
        var post = await db.Posts.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == f.LikedPost);
        Fact("liked post's likes", $"{post.LikesCount} = {await db.PostLikes.CountAsync(l => l.PostId == f.LikedPost)}");
        var list = await db.ReadingLists.AsNoTracking().SingleAsync(l => l.Id == f.FriendsList);
        Fact("followed list's followers", $"{list.FollowersCount} = {await db.ReadingListFollowers.CountAsync(x => x.ReadingListId == f.FriendsList)}");

        // What they caused others stays, without their name or photo; what they wrote stays, under the deleted account.
        var caused = await db.Notifications.AsNoTracking().Where(n => n.ActorId == id).ToListAsync();
        Fact("notifications they caused", caused.Count);
        Fact("... all under the deleted name", caused.All(n => n.ActorDisplayName == DeletedAccounts.DisplayName
            && n.ActorProfilePhoto == null && n.Message.StartsWith(DeletedAccounts.DisplayName + " ")));
        Fact("... any naming them", caused.Any(n => n.Message.Contains(f.FirstName) || n.Message.Contains(f.Renamed)));
        Fact("their comment", await db.Comments.AnyAsync(c => c.Id == f.OwnComment && c.UserId == id));
        Fact("their post", await db.Posts.AnyAsync(p => p.Id == f.OwnPost && p.UserId == id));

        // Their novels: soft-deleted, out of the rankings, gone from the site.
        var novels = await db.Novels.IgnoreQueryFilters().AsNoTracking().Where(n => n.AuthorId == id).ToListAsync();
        Fact("novels", novels.Count);
        Fact("novels hidden", novels.All(n => n.IsDeleted && !n.IsEligibleForRanking));
        Fact("novel page", (int)(await api.Get(ReaderApi.BySlug(f.OwnNovelSlug))).StatusCode);

        // Money: the balance forfeited with a ledger row, the pending withdrawal cancelled, the rest kept.
        Fact("wallet balance", (await db.UserWallets.AsNoTracking().SingleAsync(w => w.UserId == id)).CurrentBalance);
        var ledger = await db.PointTransactions.AsNoTracking().Where(t => t.UserId == id).OrderBy(t => t.CreatedAt).ToListAsync();
        Fact("ledger", string.Join(" | ", ledger.Select(t => $"{t.Type} {t.Amount} {t.BalanceBefore}->{t.BalanceAfter} {t.Description}")));
        var pending = await db.WithdrawalRequests.AsNoTracking().SingleAsync(r => r.Id == f.PendingWithdrawal);
        Fact("pending withdrawal", $"{pending.Status} {pending.RejectionReason} by {pending.ProcessedBy ?? "nobody"}, processed {pending.ProcessedAt != null}");
        Fact("approved withdrawal", (await db.WithdrawalRequests.AsNoTracking().SingleAsync(r => r.Id == f.ApprovedWithdrawal)).Status);
        Fact("Play purchase kept", await db.PlayPurchases.AnyAsync(p => p.PurchaseToken == f.PlayToken && p.UserId == id));

        // Reports about them close as AccountDeleted; the one they made stays open.
        foreach (var (name, reportId) in new[] { ("report about them", f.ReportAboutThem), ("report about their post", f.ReportAboutTheirPost), ("report they made", f.ReportTheyMade) })
        {
            var report = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == reportId);
            Fact(name, $"{report.Status} {report.Resolution} by {report.ResolvedById ?? "nobody"}, resolved {report.ResolvedAt != null}");
        }

        // Storage.
        Fact("photo in storage", api.Storage.Objects.ContainsKey(f.PhotoKey));
        Fact("banner in storage", api.Storage.Objects.ContainsKey(f.BannerKey));

        // Their sessions and every way back: no token, no sign-in, no profile, not in search; follower totals are counts
        // of what's left.
        Fact("token", (int)(await api.Get("/api/User/my-profile", f.Member)).StatusCode);
        foreach (var (what, login) in new[] { ("first name", f.FirstName), ("new name", f.Renamed), ("email", $"{f.FirstName}@example.test"), ("deleted name", row.UserName!) })
        {
            Fact($"sign-in by {what}", (int)(await api.Login(login)).StatusCode);
        }
        foreach (var (what, name) in new[] { ("first name", f.FirstName), ("new name", f.Renamed), ("deleted name", row.UserName!) })
        {
            Fact($"profile by {what}", (int)(await api.Get($"/api/User/{name}")).StatusCode);
        }
        foreach (var (what, query) in new[] { ("new name", f.Renamed), ("deleted name", row.UserName!), ("deleted display name", DeletedAccounts.DisplayName) })
        {
            var found = await (await api.Get($"/api/search/users?query={Uri.EscapeDataString(query)}")).OkJson();
            Fact($"found by {what}", found.GetProperty("items").EnumerateArray().Any(u => u.GetProperty("id").GetString() == id));
        }
        Fact("friend's followers", (await (await api.Get($"/api/User/{f.Friend.UserName}")).OkJson()).GetProperty("totalFollowers").GetInt32());
        Fact("fan's following", (await (await api.Get($"/api/User/{f.Fan.UserName}")).OkJson()).GetProperty("totalFollowing").GetInt32());

        return facts;
    }
}
