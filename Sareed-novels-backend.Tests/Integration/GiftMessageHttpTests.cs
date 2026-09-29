using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Domain.Moderation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// A short, public message with a gift (#31): sent with POST /api/gift/send, shown under the novel's recent gifts (not
/// between blocked users), in the author's notification and the sender's history, reportable, removable by a moderator
/// (the gift stays), and announced to the apps by GET /api/app/config. A refused message charges nothing.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class GiftMessageHttpTests(SardApiFactory api)
{
    // The rose of production's catalog (seeded by the migrations), 100 points.
    private static readonly Guid Rose = Guid.Parse("ec16dfde-71b8-4e23-8ff5-d1846cdf2036");

    private const string Smile = "\U0001F600";

    private async Task Fund(ApiUser user, decimal balance)
    {
        await using var db = api.Db();
        db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = user.Id, CurrentBalance = balance });
        await db.SaveChangesAsync();
    }

    private async Task<decimal> Balance(ApiUser user)
    {
        await using var db = api.Db();
        return await db.UserWallets.Where(w => w.UserId == user.Id).Select(w => w.CurrentBalance).SingleAsync();
    }

    private async Task<List<GiftTransaction>> GiftsSentBy(ApiUser sender)
    {
        await using var db = api.Db();
        return await db.GiftTransactions.AsNoTracking().Where(t => t.SenderId == sender.Id).ToListAsync();
    }

    private Task<HttpResponseMessage> SendGift(ApiUser sender, Guid novelId, string? message, int count = 1) =>
        api.Send(HttpMethod.Post, "/api/gift/send", sender, JsonContent.Create(new { giftId = Rose, novelId, count, message }));

    /// <summary>Sends a gift that must go through, and returns its record's id (the id to report its message).</summary>
    private async Task<Guid> Sent(ApiUser sender, Guid novelId, string? message)
    {
        var before = (await GiftsSentBy(sender)).Select(t => t.Id).ToHashSet();
        var result = await (await SendGift(sender, novelId, message)).OkJson();
        Assert.True(result.GetProperty("success").GetBoolean());
        return (await GiftsSentBy(sender)).Single(t => !before.Contains(t.Id)).Id;
    }

    /// <summary>A reader with points, and an author's novel for them to gift.</summary>
    private async Task<(ApiUser Reader, ApiUser Author, Novel Novel)> ReaderAndNovel(decimal balance = 1000)
    {
        var (reader, author) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author, title: "ظل الأمير " + Seed.Marker());
        await Fund(reader, balance);
        return (reader, author, novel);
    }

    /// <summary>GET /api/gift/novel/{novelId} as <paramref name="viewer"/> (anonymous when null), by gift id.</summary>
    private async Task<Dictionary<Guid, JsonElement>> NovelGifts(Guid novelId, ApiUser? viewer = null)
    {
        var page = await (await api.Get($"/api/gift/novel/{novelId}?pageSize=50", viewer)).OkJson();
        return page.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetGuid());
    }

    private static string? MessageOf(JsonElement item, string property = "message") =>
        item.GetProperty(property).ValueKind == JsonValueKind.Null ? null : item.GetProperty(property).GetString();

    /// <summary>The author's GiftReceived notifications, once the background work wrote <paramref name="expected"/>.</summary>
    private async Task<List<JsonElement>> GiftNotifications(ApiUser author, int expected)
    {
        for (var waited = 0; ; waited++)
        {
            var page = await (await api.Get("/api/notifications?pageSize=50", author)).OkJson();
            var gifts = page.GetProperty("notifications").EnumerateArray()
                .Where(n => n.GetProperty("type").GetString() == NotificationType.GiftReceived)
                .ToList();
            if (gifts.Count >= expected || waited == 150)
            {
                Assert.Equal(expected, gifts.Count);
                return gifts;
            }
            await Task.Delay(100);
        }
    }

    private static Guid? GiftTransactionIdOf(JsonElement notification) =>
        notification.GetProperty("giftTransactionId").ValueKind == JsonValueKind.Null
            ? null
            : notification.GetProperty("giftTransactionId").GetGuid();

    // ─── Sending ───

    [Fact]
    public async Task Sending_stores_the_trimmed_message_and_everyone_sees_it_under_the_novels_gifts()
    {
        var (reader, author, novel) = await ReaderAndNovel();
        var stranger = await api.SignUp();

        var withMessage = await Sent(reader, novel.Id, "  شكراً على الفصل الأخير 🌹\n  ");
        var blank = await Sent(reader, novel.Id, " \n\t ");
        var without = await Sent(reader, novel.Id, null);

        var stored = (await GiftsSentBy(reader)).ToDictionary(t => t.Id);
        Assert.Equal("شكراً على الفصل الأخير 🌹", stored[withMessage].Message);
        Assert.Null(stored[blank].Message);
        Assert.Null(stored[without].Message);
        // A message changes nothing about the gift itself: three roses, 300 points.
        Assert.Equal(700m, await Balance(reader));

        // For everyone, anonymous callers included; gifts without one have null.
        foreach (var viewer in new[] { null, stranger, reader, author })
        {
            var gifts = await NovelGifts(novel.Id, viewer);
            Assert.Equal(3, gifts.Count);
            Assert.Equal("شكراً على الفصل الأخير 🌹", MessageOf(gifts[withMessage]));
            Assert.Null(MessageOf(gifts[blank]));
            Assert.Null(MessageOf(gifts[without]));
        }
    }

    [Fact]
    public async Task Two_hundred_characters_go_and_two_hundred_and_one_are_refused_with_nothing_charged()
    {
        var (reader, _, novel) = await ReaderAndNovel();
        // 200 emoji are 400 UTF-16 units, yet 200 characters, as the app's counter counts them.
        var atLimit = string.Concat(Enumerable.Repeat(Smile, 200));

        var accepted = await Sent(reader, novel.Id, atLimit);
        Assert.Equal(atLimit, Assert.Single(await GiftsSentBy(reader)).Message);
        Assert.Equal(accepted, Assert.Single(await GiftsSentBy(reader)).Id);

        var refused = await (await SendGift(reader, novel.Id, atLimit + "ب")).Error(HttpStatusCode.BadRequest);

        Assert.Equal("GiftMessageTooLong", refused.GetProperty("code").GetString());
        Assert.Equal("الرسالة طويلة: الحد الأقصى 200 حرف.", refused.GetProperty("message").GetString());
        Assert.Equal(900m, await Balance(reader));
        Assert.Single(await GiftsSentBy(reader));
    }

    [Fact]
    public async Task An_author_who_blocked_the_sender_refuses_their_message_with_403_and_nothing_is_charged()
    {
        var (reader, author, novel) = await ReaderAndNovel();
        (await api.Block(author, reader)).EnsureSuccessStatusCode();

        var refused = await (await SendGift(reader, novel.Id, "رسالة إلى الكاتب")).Error(HttpStatusCode.Forbidden);

        Assert.Equal("Blocked", refused.GetProperty("code").GetString());
        Assert.Equal("لا يمكنك إرسال رسالة إلى هذا الكاتب.", refused.GetProperty("message").GetString());
        Assert.Equal(1000m, await Balance(reader));
        Assert.Empty(await GiftsSentBy(reader));

        // A gift without a message goes as it always did (a blank message is none).
        await Sent(reader, novel.Id, null);
        await Sent(reader, novel.Id, "   ");
        Assert.Equal(800m, await Balance(reader));
        Assert.All(await GiftsSentBy(reader), t => Assert.Null(t.Message));
    }

    // ─── Where it comes back ───

    [Fact]
    public async Task Between_blocked_users_the_message_is_null_both_ways_and_the_gift_stays_listed()
    {
        var (reader, author, novel) = await ReaderAndNovel();
        var (blocker, blockedBySender, bystander) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var gift = await Sent(reader, novel.Id, "رسالة للجميع");
        (await api.Block(blocker, reader)).EnsureSuccessStatusCode(); // a viewer who blocked the sender
        (await api.Block(reader, blockedBySender)).EnsureSuccessStatusCode(); // a viewer whom the sender blocked

        Assert.Null(MessageOf((await NovelGifts(novel.Id, blocker))[gift]));
        Assert.Null(MessageOf((await NovelGifts(novel.Id, blockedBySender))[gift]));
        foreach (var viewer in new[] { null, bystander, author, reader })
        {
            Assert.Equal("رسالة للجميع", MessageOf((await NovelGifts(novel.Id, viewer))[gift]));
        }

        // The sender's own block doesn't stop them writing to that author (only the author's block does), but the two
        // don't see it.
        var authorBlockedBySender = await api.SignUp();
        var novel2 = await api.AddNovel(authorBlockedBySender);
        (await api.Block(reader, authorBlockedBySender)).EnsureSuccessStatusCode();
        var second = await Sent(reader, novel2.Id, "رسالة ثانية");
        Assert.Null(MessageOf((await NovelGifts(novel2.Id, authorBlockedBySender))[second]));
        Assert.Equal("رسالة ثانية", MessageOf((await NovelGifts(novel2.Id))[second]));

        // Unblocking shows it again.
        (await api.Unblock(blocker, reader)).EnsureSuccessStatusCode();
        Assert.Equal("رسالة للجميع", MessageOf((await NovelGifts(novel.Id, blocker))[gift]));
    }

    [Fact]
    public async Task The_notification_carries_the_message_and_the_gift_record_to_report_it_by()
    {
        var (reader, author, novel) = await ReaderAndNovel();
        var withMessage = await Sent(reader, novel.Id, "أحببت النهاية ✨");
        var without = await Sent(reader, novel.Id, null);

        var notifications = (await GiftNotifications(author, expected: 2)).ToDictionary(n => GiftTransactionIdOf(n)!.Value);

        Assert.Equal("أحببت النهاية ✨", MessageOf(notifications[withMessage], "giftMessage"));
        Assert.Null(MessageOf(notifications[without], "giftMessage"));
        // The sentence is as it was, with the parts of #25.
        Assert.All(notifications.Values, n =>
        {
            Assert.Equal($"{reader.UserName} أرسل وردة إلى روايتك «{novel.Title}»", n.GetProperty("message").GetString());
            Assert.Equal(Rose, n.GetProperty("giftId").GetGuid());
            Assert.Equal(1, n.GetProperty("giftCount").GetInt32());
        });

        // Other notifications have neither.
        (await api.Follow(reader, author)).EnsureSuccessStatusCode();
        await api.WaitForNotificationsFrom(author, reader, count: 3);
        var page = await (await api.Get("/api/notifications?pageSize=50", author)).OkJson();
        var follow = Assert.Single(page.GetProperty("notifications").EnumerateArray(),
            n => n.GetProperty("type").GetString() == NotificationType.NewFollower);
        Assert.Equal(JsonValueKind.Null, follow.GetProperty("giftTransactionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, follow.GetProperty("giftMessage").ValueKind);
    }

    [Fact]
    public async Task My_history_returns_the_message()
    {
        var (reader, _, novel) = await ReaderAndNovel();
        var withMessage = await Sent(reader, novel.Id, "إلى الأمام دائماً");
        var without = await Sent(reader, novel.Id, null);

        var history = await (await api.Get("/api/gift/my-history", reader)).OkJson();

        var items = history.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetGuid());
        Assert.Equal("إلى الأمام دائماً", MessageOf(items[withMessage]));
        Assert.Null(MessageOf(items[without]));
    }

    [Fact]
    public async Task Other_gift_lists_carry_no_messages()
    {
        var (reader, _, novel) = await ReaderAndNovel();
        await Sent(reader, novel.Id, "رسالة");

        var supporters = await (await api.Get($"/api/gift/novel/{novel.Id}/top-supporters")).OkJson();

        var supporter = Assert.Single(supporters.EnumerateArray());
        Assert.False(supporter.TryGetProperty("message", out _));
    }

    // ─── Moderation ───

    [Fact]
    public async Task Reporting_a_gift_message_keeps_its_text_and_the_sender_cannot_report_their_own()
    {
        var (reader, author, novel) = await ReaderAndNovel();
        var admin = await api.SignUpAdmin();
        var gift = await Sent(reader, novel.Id, "كلام مسيء في رسالة هدية");
        var plain = await Sent(reader, novel.Id, null);

        // The author (and any signed-in member) can report it.
        var reportId = await api.Reported(author, "GiftMessage", gift, "Harassment");

        await using (var db = api.Db())
        {
            var report = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == reportId);
            Assert.Equal(ReportTargetType.GiftMessage, report.TargetType);
            Assert.Equal(gift, report.TargetId);
            Assert.Equal(reader.Id, report.TargetOwnerId);
            Assert.Equal("كلام مسيء في رسالة هدية", report.TargetExcerpt);
        }
        Assert.Equal(HttpStatusCode.Created, (await api.Report(await api.SignUp(), "giftmessage", gift)).StatusCode);

        // Not the sender; and a gift without a message has nothing to report.
        var own = await (await api.Report(reader, "GiftMessage", gift)).Error(HttpStatusCode.BadRequest);
        Assert.Equal("CannotReportOwnContent", own.GetProperty("code").GetString());
        var none = await (await api.Report(author, "GiftMessage", plain)).Error(HttpStatusCode.NotFound);
        Assert.Equal("TargetNotFound", none.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Report(null!, "GiftMessage", gift)).StatusCode);

        // The moderators see it with its text, where it shows, and whose it is (All lists the newest first).
        var listed = (await api.AdminReports(admin, "All")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == reportId);
        var target = listed.GetProperty("target");
        Assert.Equal("GiftMessage", target.GetProperty("type").GetString());
        Assert.Equal(gift, target.GetProperty("id").GetGuid());
        Assert.False(target.GetProperty("isDeleted").GetBoolean());
        Assert.Equal("كلام مسيء في رسالة هدية", target.GetProperty("excerpt").GetString());
        Assert.Equal("كلام مسيء في رسالة هدية", target.GetProperty("currentExcerpt").GetString());
        Assert.Equal($"/novel/{novel.Slug}", target.GetProperty("link").GetString());
        Assert.Equal(reader.Id, target.GetProperty("owner").GetProperty("userId").GetString());
        Assert.Equal(2, listed.GetProperty("openReportsOnTarget").GetInt32());
    }

    [Fact]
    public async Task Once_an_admin_removes_the_message_it_is_null_everywhere_and_the_gift_stays()
    {
        var (reader, author, novel) = await ReaderAndNovel();
        var admin = await api.SignUpAdmin();
        var gift = await Sent(reader, novel.Id, "رسالة مخالفة");
        await GiftNotifications(author, expected: 1);
        var authorBalance = await Balance(author);
        var reportId = await api.Reported(author, "GiftMessage", gift, "Spam");

        var result = await (await api.Resolve(admin, reportId, "RemoveContent")).OkJson();

        Assert.True(result.GetProperty("contentRemoved").GetBoolean());
        Assert.Equal(1, result.GetProperty("resolvedReports").GetInt32());
        // The gift, its payment and the author's earning stay; only the message is gone.
        var listed = (await NovelGifts(novel.Id))[gift];
        Assert.Null(MessageOf(listed));
        Assert.Equal(1, listed.GetProperty("count").GetInt32());
        Assert.Equal(authorBalance, await Balance(author));
        Assert.Equal(900m, await Balance(reader));
        var notification = Assert.Single(await GiftNotifications(author, expected: 1));
        Assert.Equal(gift, GiftTransactionIdOf(notification));
        Assert.Null(MessageOf(notification, "giftMessage"));
        var history = await (await api.Get("/api/gift/my-history", reader)).OkJson();
        Assert.Null(MessageOf(Assert.Single(history.GetProperty("items").EnumerateArray())));

        // The report keeps what it said; the target reads as removed, and there's nothing left to report or remove.
        var closed = (await api.AdminReports(admin, "Resolved")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == reportId);
        Assert.True(closed.GetProperty("target").GetProperty("isDeleted").GetBoolean());
        Assert.Equal("رسالة مخالفة", closed.GetProperty("target").GetProperty("excerpt").GetString());
        Assert.Equal("RemoveContent", closed.GetProperty("action").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Report(await api.SignUp(), "GiftMessage", gift)).StatusCode);
    }

    [Fact]
    public async Task A_suspended_member_cannot_send_a_message_and_is_charged_nothing()
    {
        var (reader, author, novel) = await ReaderAndNovel();
        var admin = await api.SignUpAdmin();
        var gift = await Sent(reader, novel.Id, "رسالة أولى");
        var reportId = await api.Reported(author, "GiftMessage", gift, "Harassment");

        // Suspending the message's sender, from its report.
        var result = await (await api.Resolve(admin, reportId, "SuspendUser", suspensionDays: 3)).OkJson();
        Assert.Equal(reader.Id, result.GetProperty("suspendedUserId").GetString());

        // Like a comment, a gift needs a session, and a suspended member's are refused.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendGift(reader, novel.Id, "رسالة ثانية")).StatusCode);
        Assert.Equal(900m, await Balance(reader));
        Assert.Single(await GiftsSentBy(reader));
        // What they wrote before stays, as their comments do (removing it is a separate action).
        Assert.Equal("رسالة أولى", MessageOf((await NovelGifts(novel.Id))[gift]));
    }

    [Fact]
    public async Task A_deleted_account_keeps_its_messages_under_the_deleted_name_as_its_comments()
    {
        var (reader, author, novel) = await ReaderAndNovel();
        var gift = await Sent(reader, novel.Id, "رسالة من قارئ");
        await GiftNotifications(author, expected: 1);
        var reportId = await api.Reported(author, "GiftMessage", gift);

        var deleted = await api.Send(HttpMethod.Delete, "/api/User/me", reader, JsonContent.Create(new { password = ModerationApi.Password }));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var listed = (await NovelGifts(novel.Id))[gift];
        Assert.Equal("رسالة من قارئ", MessageOf(listed));
        Assert.Equal("مستخدم محذوف", listed.GetProperty("senderDisplayName").GetString());
        Assert.StartsWith("deleted-", listed.GetProperty("senderUserName").GetString());
        var notification = Assert.Single(await GiftNotifications(author, expected: 1));
        Assert.Equal("رسالة من قارئ", MessageOf(notification, "giftMessage"));
        Assert.StartsWith("مستخدم محذوف", notification.GetProperty("message").GetString());
        // Open reports about their content close with the account, as for comments.
        await using var db = api.Db();
        var report = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == reportId);
        Assert.Equal(ReportAction.AccountDeleted, report.Resolution);
    }

    // ─── Telling the apps: GET /api/app/config ───

    [Fact]
    public async Task The_app_config_says_how_long_a_message_can_be()
    {
        var config = await (await api.Get("/api/app/config")).OkJson();

        Assert.Equal(200, config.GetProperty("gifts").GetProperty("messageMaxLength").GetInt32());
    }

    private WebApplicationFactory<Program> WithMaxLength(string maxLength) =>
        api.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:Gifts:MessageMaxLength"] = maxLength })));

    [Fact]
    public async Task A_configured_limit_is_the_one_the_apps_get_and_the_one_the_server_applies()
    {
        await using var configured = WithMaxLength("150");
        var (reader, _, novel) = await ReaderAndNovel();

        var config = await (await configured.CreateClient().GetAsync("/api/app/config")).OkJson();
        Assert.Equal(150, config.GetProperty("gifts").GetProperty("messageMaxLength").GetInt32());

        Task<HttpResponseMessage> Send(string message)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/gift/send")
            {
                Content = JsonContent.Create(new { giftId = Rose, novelId = novel.Id, count = 1, message })
            };
            request.Headers.Authorization = new("Bearer", reader.Token);
            return configured.CreateClient().SendAsync(request);
        }

        var refused = await (await Send(new string('ب', 151))).Error(HttpStatusCode.BadRequest);
        Assert.Equal("GiftMessageTooLong", refused.GetProperty("code").GetString());
        Assert.Equal("الرسالة طويلة: الحد الأقصى 150 حرف.", refused.GetProperty("message").GetString());
        await (await Send(new string('ب', 150))).OkJson();
        Assert.Equal(900m, await Balance(reader));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    public async Task A_misconfigured_limit_is_an_error_not_a_wrong_answer(string maxLength)
    {
        await using var misconfigured = WithMaxLength(maxLength);

        var response = await misconfigured.CreateClient().GetAsync("/api/app/config");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
