using System.Net;
using System.Net.Http.Json;
using Domain.Constants;
using Domain.Entities;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The types filter of GET /api/notifications and GET /api/notifications/unread-count (#78), for writer mode's «على
/// رواياتي»: with unreadOnly and paging, in SQL, known names in any letter case, unknown ones ignored, and blocks as
/// before. The parsing alone: Unit/NotificationTypeFilterTests.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class NotificationTypesHttpTests(SardApiFactory api)
{
    // Production's catalog: a rose costs 100 points.
    private static readonly Guid Rose = Guid.Parse("ec16dfde-71b8-4e23-8ff5-d1846cdf2036");

    /// <summary>What the app asks for on «على رواياتي»: what happens on her novels, apart from the social ones.</summary>
    private const string OnMyNovels = "CommentOnChapter,ReviewOnNovel,GiftReceived,PrivilegeSubscribed";

    private sealed record Inbox(ApiUser User, Dictionary<string, Notification> ByName);

    /// <summary>
    /// Nine notifications, n1 the oldest and n9 the newest, a second apart. Of «على رواياتي»'s types: n1, n2, n4, n5, n7
    /// and n8, of which n1, n4, n7 and n8 are unread. Unread in all: n1, n3, n4, n6, n7 and n8.
    /// </summary>
    private async Task<Inbox> SeedInbox()
    {
        var (user, actor) = (await api.SignUp(), await api.SignUp());
        (string Type, bool IsRead)[] rows =
        [
            (NotificationType.CommentOnChapter, false), // n1
            (NotificationType.ReviewOnNovel, true), // n2
            (NotificationType.NewFollower, false), // n3
            (NotificationType.GiftReceived, false), // n4
            (NotificationType.CommentOnChapter, true), // n5
            (NotificationType.LikeOnComment, false), // n6
            (NotificationType.PrivilegeSubscribed, false), // n7
            (NotificationType.ReviewOnNovel, false), // n8
            (NotificationType.NewFollower, true), // n9
        ];
        var start = DateTime.UtcNow.AddHours(-1);
        var notices = rows.Select((row, i) => new Notification
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Type = row.Type,
            ActorId = actor.Id,
            ActorDisplayName = actor.UserName,
            Message = "إشعار " + (i + 1),
            ActionUrl = $"/profile/{actor.UserName}",
            IsRead = row.IsRead,
            CreatedAt = start.AddSeconds(i)
        }).ToList();

        await using var db = api.Db();
        db.Notifications.AddRange(notices);
        await db.SaveChangesAsync();
        return new Inbox(user, notices.Select((n, i) => ($"n{i + 1}", n)).ToDictionary(p => p.Item1, p => p.n));
    }

    private sealed record Page(List<Guid> Ids, List<string> Types, int TotalCount, int UnreadCount, int PageNumber, int TotalPages);

    private async Task<Page> List(ApiUser user, string query)
    {
        var body = await (await api.Get($"/api/notifications?{query}", user)).OkJson();
        var items = body.GetProperty("notifications").EnumerateArray().ToList();
        return new Page(
            items.Select(n => n.GetProperty("id").GetGuid()).ToList(),
            items.Select(n => n.GetProperty("type").GetString()!).ToList(),
            body.GetProperty("totalCount").GetInt32(),
            body.GetProperty("unreadCount").GetInt32(),
            body.GetProperty("pageNumber").GetInt32(),
            body.GetProperty("totalPages").GetInt32());
    }

    private async Task<int> UnreadCount(ApiUser user, string query = "") =>
        (await (await api.Get($"/api/notifications/unread-count{query}", user)).OkJson()).GetProperty("unreadCount").GetInt32();

    private static List<Guid> Ids(Inbox inbox, params string[] names) => names.Select(name => inbox.ByName[name].Id).ToList();

    private static string Escaped(string types) => Uri.EscapeDataString(types);

    /// <summary>The page holds exactly these notifications, in this order, and counts as given.</summary>
    private static void AssertPage(Page page, List<Guid> ids, int totalCount, int unreadCount, int totalPages)
    {
        Assert.Equal(ids, page.Ids);
        Assert.Equal((totalCount, unreadCount, totalPages), (page.TotalCount, page.UnreadCount, page.TotalPages));
    }

    [Fact]
    public async Task The_filter_lists_only_those_types_newest_first_with_or_without_unread_only()
    {
        var inbox = await SeedInbox();

        var filtered = await List(inbox.User, $"types={OnMyNovels}");
        AssertPage(filtered, Ids(inbox, "n8", "n7", "n5", "n4", "n2", "n1"), totalCount: 6, unreadCount: 4, totalPages: 1);
        Assert.Equal(1, filtered.PageNumber);

        AssertPage(await List(inbox.User, $"types={OnMyNovels}&unreadOnly=true"),
            Ids(inbox, "n8", "n7", "n4", "n1"), totalCount: 4, unreadCount: 4, totalPages: 1);

        // Explicitly not unread only is the same as leaving it out.
        AssertPage(await List(inbox.User, $"types={OnMyNovels}&unreadOnly=false"),
            Ids(inbox, "n8", "n7", "n5", "n4", "n2", "n1"), totalCount: 6, unreadCount: 4, totalPages: 1);

        // Without the filter, as before: every type, and the unread count of every type.
        AssertPage(await List(inbox.User, ""),
            Ids(inbox, "n9", "n8", "n7", "n6", "n5", "n4", "n3", "n2", "n1"), totalCount: 9, unreadCount: 6, totalPages: 1);
        AssertPage(await List(inbox.User, "unreadOnly=true"),
            Ids(inbox, "n8", "n7", "n6", "n4", "n3", "n1"), totalCount: 6, unreadCount: 6, totalPages: 1);

        // One type alone, and the social ones the screen leaves out.
        AssertPage(await List(inbox.User, $"types={NotificationType.NewFollower}"),
            Ids(inbox, "n9", "n3"), totalCount: 2, unreadCount: 1, totalPages: 1);
        AssertPage(await List(inbox.User, "types=NewFollower,LikeOnComment&unreadOnly=true"),
            Ids(inbox, "n6", "n3"), totalCount: 2, unreadCount: 2, totalPages: 1);
        AssertPage(await List(inbox.User, "types=ReplyToComment"), [], totalCount: 0, unreadCount: 0, totalPages: 0);
    }

    [Fact]
    public async Task Pages_of_a_filtered_list_hold_exactly_its_notifications()
    {
        var inbox = await SeedInbox();

        var first = await List(inbox.User, $"types={OnMyNovels}&pageSize=4");
        var second = await List(inbox.User, $"types={OnMyNovels}&pageSize=4&pageNumber=2");
        var past = await List(inbox.User, $"types={OnMyNovels}&pageSize=4&pageNumber=3");
        AssertPage(first, Ids(inbox, "n8", "n7", "n5", "n4"), totalCount: 6, unreadCount: 4, totalPages: 2);
        AssertPage(second, Ids(inbox, "n2", "n1"), totalCount: 6, unreadCount: 4, totalPages: 2);
        AssertPage(past, [], totalCount: 6, unreadCount: 4, totalPages: 2);
        Assert.Equal((1, 2, 3), (first.PageNumber, second.PageNumber, past.PageNumber));

        AssertPage(await List(inbox.User, $"types={OnMyNovels}&unreadOnly=true&pageSize=3"),
            Ids(inbox, "n8", "n7", "n4"), totalCount: 4, unreadCount: 4, totalPages: 2);
        AssertPage(await List(inbox.User, $"types={OnMyNovels}&unreadOnly=true&pageSize=3&pageNumber=2"),
            Ids(inbox, "n1"), totalCount: 4, unreadCount: 4, totalPages: 2);

        // A page of one, all the way down: each notification of those types once, in order.
        var oneByOne = new List<Guid>();
        for (var page = 1; page <= 6; page++)
        {
            var single = await List(inbox.User, $"types={OnMyNovels}&pageSize=1&pageNumber={page}");
            Assert.Equal((6, 6), (single.TotalCount, single.TotalPages));
            oneByOne.AddRange(single.Ids);
        }
        Assert.Equal(Ids(inbox, "n8", "n7", "n5", "n4", "n2", "n1"), oneByOne);
    }

    [Fact]
    public async Task The_unread_count_takes_the_same_filter()
    {
        var inbox = await SeedInbox();

        Assert.Equal(4, await UnreadCount(inbox.User, $"?types={OnMyNovels}"));
        Assert.Equal(1, await UnreadCount(inbox.User, "?types=NewFollower"));
        Assert.Equal(2, await UnreadCount(inbox.User, "?types=NewFollower,LikeOnComment"));
        Assert.Equal(1, await UnreadCount(inbox.User, "?types=GiftReceived"));
        Assert.Equal(0, await UnreadCount(inbox.User, "?types=ReplyToComment"));
        Assert.Equal(6, await UnreadCount(inbox.User));

        // Reading one changes the filtered count and the list's own count alike.
        var gift = inbox.ByName["n4"].Id;
        (await api.Send(HttpMethod.Patch, $"/api/notifications/{gift}/read", inbox.User)).EnsureSuccessStatusCode();
        Assert.Equal(3, await UnreadCount(inbox.User, $"?types={OnMyNovels}"));
        Assert.Equal(3, (await List(inbox.User, $"types={OnMyNovels}")).UnreadCount);
        Assert.Equal(0, await UnreadCount(inbox.User, "?types=GiftReceived"));
        Assert.Equal(5, await UnreadCount(inbox.User));
    }

    [Fact]
    public async Task Unknown_names_are_ignored_and_naming_no_known_type_means_every_type()
    {
        var inbox = await SeedInbox();
        var comments = Ids(inbox, "n5", "n1");

        Assert.Equal(comments, (await List(inbox.User, "types=CommentOnChapter,NotAType")).Ids);
        Assert.Equal(1, await UnreadCount(inbox.User, "?types=CommentOnChapter,NotAType"));

        foreach (var types in new[] { "NotAType", "NotAType,AnotherOne", "", Escaped(" , ,"), Escaped("Comment On Chapter") })
        {
            var every = await List(inbox.User, $"types={types}");
            Assert.Equal((9, 6), (every.TotalCount, every.UnreadCount));
            Assert.Equal(6, await UnreadCount(inbox.User, $"?types={types}"));
            Assert.Equal(6, (await List(inbox.User, $"types={types}&unreadOnly=true")).TotalCount);
        }
    }

    [Fact]
    public async Task Names_match_in_any_letter_case_with_spaces_around_them_and_the_parameter_may_repeat()
    {
        var inbox = await SeedInbox();
        var expected = Ids(inbox, "n8", "n5", "n2", "n1");

        var page = await List(inbox.User, $"types={Escaped(" commentonchapter , REVIEWONNOVEL ")}");
        Assert.Equal(expected, page.Ids);
        Assert.Equal((4, 2), (page.TotalCount, page.UnreadCount));
        Assert.All(page.Types, type => Assert.Contains(type, new[] { NotificationType.CommentOnChapter, NotificationType.ReviewOnNovel }));
        Assert.Equal(2, await UnreadCount(inbox.User, $"?types={Escaped("Reviewonnovel, commentONchapter")}"));

        Assert.Equal(expected, (await List(inbox.User, "types=CommentOnChapter&types=reviewOnNovel")).Ids);
        Assert.Equal(2, await UnreadCount(inbox.User, "?types=CommentOnChapter&types=reviewOnNovel"));
    }

    [Fact]
    public async Task Blocks_stop_the_notifications_they_stopped_before_whatever_the_filter()
    {
        var (me, them, control, reader) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(me);
        var (chapter, _) = await api.AddChapter(novel, "<p>فقرة</p>");
        await using (var db = api.Db())
        {
            db.UserWallets.AddRange(
                new UserWallet { Id = Guid.NewGuid(), UserId = them.Id, CurrentBalance = 1000 },
                new UserWallet { Id = Guid.NewGuid(), UserId = reader.Id, CurrentBalance = 1000 });
            await db.SaveChangesAsync();
        }

        // I blocked them: neither their comment nor their gift notifies me (a gift still goes through, without a message).
        // The reader blocked me: their gift still notifies me (an author learns of payments, #52), unlike a comment would.
        await api.Block(me, them);
        await api.Block(reader, me);
        foreach (var actor in new[] { them, control })
        {
            await api.Comment(actor, $"/api/comment/chapter/{chapter.Id}", "تعليق على فصلي");
        }
        foreach (var sender in new[] { them, reader })
        {
            (await api.Send(HttpMethod.Post, "/api/gift/send", sender, JsonContent.Create(new { giftId = Rose, novelId = novel.Id, count = 1 })))
                .EnsureSuccessStatusCode();
        }
        await api.WaitForNotificationsFrom(me, control);
        await api.WaitForNotificationsFrom(me, reader);
        await Task.Delay(500);
        Assert.Empty(await api.NotificationsFrom(me, them));

        var onMyNovels = await List(me, $"types={OnMyNovels}");
        Assert.Equal((2, 2), (onMyNovels.TotalCount, onMyNovels.UnreadCount));
        Assert.Equal([NotificationType.CommentOnChapter, NotificationType.GiftReceived], onMyNovels.Types.Order());
        Assert.Equal((1, 1), ((await List(me, "types=CommentOnChapter")).TotalCount, await UnreadCount(me, "?types=CommentOnChapter")));
        var gifts = await (await api.Get("/api/notifications?types=GiftReceived", me)).OkJson();
        var gift = Assert.Single(gifts.GetProperty("notifications").EnumerateArray());
        Assert.Equal(reader.Id, gift.GetProperty("actorId").GetString());
        Assert.Equal(1, await UnreadCount(me, "?types=GiftReceived"));
        Assert.Equal(2, await UnreadCount(me, $"?types={OnMyNovels}"));
        Assert.Equal(2, await UnreadCount(me));
    }

    [Fact]
    public async Task Signed_out_it_is_401_with_or_without_the_filter()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get($"/api/notifications?types={OnMyNovels}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get($"/api/notifications/unread-count?types={OnMyNovels}")).StatusCode);
    }
}
