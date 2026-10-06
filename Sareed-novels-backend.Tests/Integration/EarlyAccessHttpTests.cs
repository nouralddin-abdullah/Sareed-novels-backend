using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Early access by chapter (#94) through the API: a chapter is locked by its own lock, started when early access was
/// turned on (the chapters it locked then) or when the chapter came out while it was on, and lasts the novel's days or,
/// subscribers only, until the author frees it. Unpublishing, publishing again, reordering and deleting never lock or
/// free another chapter (the positional window they used to move); a chapter freed never locks again. The rules alone:
/// Unit/EarlyAccessRulesTests.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class EarlyAccessHttpTests(SardApiFactory api)
{
    private sealed record Shelf(ApiUser Author, Novel Novel, List<Chapter> Chapters)
    {
        /// <summary>The chapter seeded at reading position <paramref name="position"/> (1-based).</summary>
        public Chapter At(int position) => Chapters[position - 1];

        public string Privilege => $"/api/novel/{Novel.Id}/privilege";
        public string Chapter(Chapter chapter) => $"/api/novel/{Novel.Id}/chapter/{chapter.Id}";
    }

    private sealed record Lock(DateTime? From, DateTime? FreedAt);

    private sealed record Listed(bool IsLocked, DateTime? UnlocksAt);

    private static readonly string[] PrivilegeFields =
    [
        "isEnabled", "subscriptionCost", "earlyAccessDays", "subscribersOnly", "lockedChaptersCount", "nextUnlockAt",
        "privilegeStartSequence", "totalPublishedChapters", "subscribersCount", "isSubscribed", "subscribedAt", "canCancel"
    ];

    /// <summary>
    /// A public novel of a new author with <paramref name="published"/> published chapters (reading positions 1 to n, which
    /// came out two hours ago, a minute apart) and <paramref name="drafts"/> drafts after them.
    /// </summary>
    private async Task<Shelf> SeedNovel(int published, int drafts = 0)
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var start = DateTime.UtcNow.AddHours(-2);
        var chapters = Seed.Chapters(novel, published, start)
            .Concat(Seed.Chapters(novel, drafts, start, status: ChapterStatuses.Draft, startIndex: published + 1))
            .ToList();
        foreach (var chapter in chapters.Where(c => c.Status == ChapterStatuses.Published))
        {
            chapter.PublishedChapterSequence = chapter.ChapterIndex;
        }

        await using var db = api.Db();
        db.Chapters.AddRange(chapters);
        await db.SaveChangesAsync();
        return new Shelf(author, novel, chapters);
    }

    private async Task<HttpResponseMessage> Enable(Shelf shelf, object body, ApiUser? caller = null) =>
        await api.Send(HttpMethod.Post, shelf.Privilege + "/enable", caller ?? shelf.Author, JsonContent.Create(body));

    private async Task<HttpResponseMessage> Change(Shelf shelf, object body, ApiUser? caller = null) =>
        await api.Send(HttpMethod.Patch, shelf.Privilege, caller ?? shelf.Author, JsonContent.Create(body));

    private async Task<HttpResponseMessage> Unlock(Shelf shelf, Chapter chapter, ApiUser? caller = null) =>
        await api.Send(HttpMethod.Post, $"{shelf.Privilege}/manual-unlock/{chapter.Id}", caller ?? shelf.Author);

    private async Task<HttpResponseMessage> Disable(Shelf shelf, ApiUser? caller = null) =>
        await api.Send(HttpMethod.Post, shelf.Privilege + "/disable", caller ?? shelf.Author);

    /// <summary>A 400 answer's code, its message checked to be Arabic.</summary>
    private static async Task<string> Refused(HttpResponseMessage response)
    {
        var body = await response.Error(HttpStatusCode.BadRequest);
        Assert.Matches("[؀-ۿ]", body.GetProperty("message").GetString());
        return body.GetProperty("code").GetString()!;
    }

    private static async Task Ok(HttpResponseMessage response) =>
        Assert.True((await response.OkJson()).GetProperty("success").GetBoolean());

    private async Task<JsonElement> Privilege(Shelf shelf, ApiUser? viewer) => await (await api.Get(shelf.Privilege, viewer)).OkJson();

    private static DateTime? Date(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetDateTime();

    private static int? Number(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();

    /// <summary>The novel's reader list (GET /api/novel/{id}/chapter) as <paramref name="viewer"/> sees it, by chapter.</summary>
    private async Task<Dictionary<Guid, Listed>> ReaderList(Shelf shelf, ApiUser? viewer) =>
        Listing(await (await api.Get($"/api/novel/{shelf.Novel.Id}/chapter", viewer)).OkJson());

    /// <summary>The author's list (GET /api/myworks/{id}/chapters), by chapter.</summary>
    private async Task<Dictionary<Guid, Listed>> AuthorList(Shelf shelf) =>
        Listing(await (await api.Get($"/api/myworks/{shelf.Novel.Id}/chapters", shelf.Author)).OkJson());

    private static Dictionary<Guid, Listed> Listing(JsonElement list) => list.EnumerateArray().ToDictionary(
        item => item.GetProperty("id").GetGuid(),
        item => new Listed(item.GetProperty("isLocked").GetBoolean(), Date(item.GetProperty("unlocksAt"))));

    /// <summary>Each chapter's lock as stored, by chapter.</summary>
    private async Task<Dictionary<Guid, Lock>> StoredLocks(Shelf shelf)
    {
        await using var db = api.Db();
        return await db.Chapters.AsNoTracking().Where(c => c.NovelId == shelf.Novel.Id)
            .ToDictionaryAsync(c => c.Id, c => new Lock(c.EarlyAccessFrom, c.EarlyAccessFreedAt));
    }

    /// <summary>Moves a chapter's lock start, as if it had locked then.</summary>
    private async Task LockedSince(Chapter chapter, DateTime from)
    {
        await using var db = api.Db();
        await db.Chapters.Where(c => c.Id == chapter.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.EarlyAccessFrom, from));
    }

    private async Task<ApiUser> Subscriber(Shelf shelf)
    {
        var reader = await api.SignUp();
        await using (var db = api.Db())
        {
            db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = reader.Id, CurrentBalance = 5000 });
            await db.SaveChangesAsync();
        }
        await Ok(await api.Send(HttpMethod.Post, shelf.Privilege + "/subscribe", reader));
        return reader;
    }

    private static IEnumerable<int> Positions(int from, int to) => Enumerable.Range(from, to - from + 1);

    // ===== Turning it on =====

    [Fact]
    public async Task Enabling_locks_the_last_chapters_from_now_each_for_the_days_chosen()
    {
        var shelf = await SeedNovel(published: 25);
        var reader = await api.SignUp();

        var before = DateTime.UtcNow;
        await Ok(await Enable(shelf, new { subscriptionCost = 200, earlyAccessDays = 3 }));
        var after = DateTime.UtcNow;

        // The last min(20, 25 - 10) chapters, positions 11 to 25, lock from now; the first 10 stay free.
        var locks = await StoredLocks(shelf);
        Assert.All(Positions(1, 10), p => Assert.Equal(new Lock(null, null), locks[shelf.At(p).Id]));
        Assert.All(Positions(11, 25), p => Assert.InRange(locks[shelf.At(p).Id].From!.Value, before, after));
        var from = locks[shelf.At(11).Id].From!.Value;
        Assert.All(Positions(11, 25), p => Assert.Equal(new Lock(from, null), locks[shelf.At(p).Id]));

        var info = await Privilege(shelf, shelf.Author);
        Assert.Equal(PrivilegeFields.Order(), info.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal((200m, 3, false, 15, from.AddDays(3), 11, 25, (int?)0),
            (info.GetProperty("subscriptionCost").GetDecimal(), info.GetProperty("earlyAccessDays").GetInt32(),
                info.GetProperty("subscribersOnly").GetBoolean(), info.GetProperty("lockedChaptersCount").GetInt32(),
                Date(info.GetProperty("nextUnlockAt")), info.GetProperty("privilegeStartSequence").GetInt32(),
                info.GetProperty("totalPublishedChapters").GetInt32(), Number(info.GetProperty("subscribersCount"))));
        Assert.EndsWith("Z", info.GetProperty("nextUnlockAt").GetString());

        // How many subscribed is the author's to know.
        Assert.Null(Number((await Privilege(shelf, reader)).GetProperty("subscribersCount")));
        Assert.Null(Number((await Privilege(shelf, null)).GetProperty("subscribersCount")));

        var list = await ReaderList(shelf, null);
        Assert.All(Positions(1, 10), p => Assert.Equal(new Listed(false, null), list[shelf.At(p).Id]));
        Assert.All(Positions(11, 25), p => Assert.Equal(new Listed(true, from.AddDays(3)), list[shelf.At(p).Id]));
    }

    [Fact]
    public async Task Enabling_from_a_chapter_for_subscribers_only_and_what_it_refuses()
    {
        var shelf = await SeedNovel(published: 25);

        Assert.Equal("ValidationFailed", await Refused(await Enable(shelf, new { subscriptionCost = 200, earlyAccessDays = 5, subscribersOnly = true })));
        Assert.Equal("InvalidEarlyAccessDays", await Refused(await Enable(shelf, new { subscriptionCost = 200, earlyAccessDays = 0 })));
        Assert.Equal("InvalidEarlyAccessDays", await Refused(await Enable(shelf, new { subscriptionCost = 200, earlyAccessDays = 31 })));
        Assert.Equal("InvalidSubscriptionCost", await Refused(await Enable(shelf, new { subscriptionCost = 50 })));
        Assert.Equal("FirstChaptersMustStayFree", await Refused(await Enable(shelf, new { subscriptionCost = 200, privilegeStartSequence = 10 })));
        var beyond = await Enable(shelf, new { subscriptionCost = 200, privilegeStartSequence = 26 });
        Assert.Contains("من 11 إلى 25", (await beyond.Error(HttpStatusCode.BadRequest)).GetProperty("message").GetString());
        Assert.Equal("NotOwner", await Refused(await Enable(shelf, new { subscriptionCost = 200 }, await api.SignUp())));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.Send(HttpMethod.Post, shelf.Privilege + "/enable", null, JsonContent.Create(new { subscriptionCost = 200 }))).StatusCode);
        Assert.False((await Privilege(shelf, null)).GetProperty("isEnabled").GetBoolean());

        await Ok(await Enable(shelf, new { subscriptionCost = 300, privilegeStartSequence = 20, subscribersOnly = true }));

        var info = await Privilege(shelf, null);
        Assert.Equal(((int?)null, true, 6, (DateTime?)null, 20),
            (Number(info.GetProperty("earlyAccessDays")), info.GetProperty("subscribersOnly").GetBoolean(),
                info.GetProperty("lockedChaptersCount").GetInt32(), Date(info.GetProperty("nextUnlockAt")),
                info.GetProperty("privilegeStartSequence").GetInt32()));
        var list = await ReaderList(shelf, null);
        Assert.All(Positions(1, 19), p => Assert.False(list[shelf.At(p).Id].IsLocked));
        Assert.All(Positions(20, 25), p => Assert.Equal(new Listed(true, null), list[shelf.At(p).Id]));
        Assert.Equal("PrivilegeAlreadyEnabled", await Refused(await Enable(shelf, new { subscriptionCost = 200 })));

        // Too few chapters, or too many from that start.
        Assert.Equal("NotEnoughPublishedChapters", await Refused(await Enable(await SeedNovel(published: 10, drafts: 3), new { subscriptionCost = 200 })));
        Assert.Equal("TooManyLockedChapters",
            await Refused(await Enable(await SeedNovel(published: 31), new { subscriptionCost = 200, privilegeStartSequence = 11 })));

        // The website before #94 sends neither: 7 days.
        var web = await SeedNovel(published: 12);
        await Ok(await Enable(web, new { subscriptionCost = 100, privilegeStartSequence = 11 }));
        var fromWeb = await Privilege(web, null);
        Assert.Equal((7, false, 2), (fromWeb.GetProperty("earlyAccessDays").GetInt32(), fromWeb.GetProperty("subscribersOnly").GetBoolean(),
            fromWeb.GetProperty("lockedChaptersCount").GetInt32()));
    }

    [Fact]
    public async Task Two_enables_at_once_turn_it_on_once()
    {
        var shelf = await SeedNovel(published: 12);

        var answers = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Enable(shelf, new { subscriptionCost = 200 })));

        Assert.Single(answers, a => a.StatusCode == HttpStatusCode.OK);
        foreach (var refused in answers.Where(a => a.StatusCode != HttpStatusCode.OK))
        {
            Assert.Equal("PrivilegeAlreadyEnabled", await Refused(refused));
        }
        await using var db = api.Db();
        Assert.Equal(1, await db.NovelPrivileges.CountAsync(p => p.NovelId == shelf.Novel.Id));
    }

    // ===== How long a lock lasts =====

    [Fact]
    public async Task A_lock_ends_its_days_after_its_own_start()
    {
        var shelf = await SeedNovel(published: 15);
        await Ok(await Enable(shelf, new { subscriptionCost = 200, earlyAccessDays = 3 }));
        var now = DateTime.UtcNow;
        var (over, ending) = (now.AddDays(-3).AddMinutes(-1), now.AddDays(-2));
        await LockedSince(shelf.At(11), over);
        await LockedSince(shelf.At(12), ending);

        var list = await ReaderList(shelf, null);
        Assert.Equal(new Listed(false, null), list[shelf.At(11).Id]);
        Assert.Equal(new Listed(true, ending.AddDays(3)), list[shelf.At(12).Id]);
        Assert.All(Positions(13, 15), p => Assert.True(list[shelf.At(p).Id].IsLocked));

        var info = await Privilege(shelf, null);
        Assert.Equal((4, ending.AddDays(3), 12), (info.GetProperty("lockedChaptersCount").GetInt32(),
            Date(info.GetProperty("nextUnlockAt")), info.GetProperty("privilegeStartSequence").GetInt32()));

        // The chapter itself: open once its lock is over, closed with when it opens until then.
        Assert.False((await (await api.Get(shelf.Chapter(shelf.At(11)))).OkJson()).GetProperty("isLocked").GetBoolean());
        var closed = await (await api.Get(shelf.Chapter(shelf.At(12)))).OkJson();
        Assert.Equal((true, ending.AddDays(3)), (closed.GetProperty("isLocked").GetBoolean(), Date(closed.GetProperty("unlocksAt"))));
        Assert.Matches("[؀-ۿ]", closed.GetProperty("lockMessage").GetString());
        Assert.Empty(closed.GetProperty("paragraphs").EnumerateArray());
    }

    // ===== Chapters coming out =====

    [Fact]
    public async Task A_chapter_that_comes_out_locks_from_when_it_came_out_unless_among_the_first_ten()
    {
        var shelf = await SeedNovel(published: 12, drafts: 2);
        await Ok(await Enable(shelf, new { subscriptionCost = 200, earlyAccessDays = 7 }));
        var (atTheEnd, earlier) = (shelf.At(13), shelf.At(14));

        // Published at the end: it locks from when it came out.
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, shelf.Chapter(atTheEnd), shelf.Author,
            JsonContent.Create(new { status = ChapterStatuses.Published }))).StatusCode);
        await using (var db = api.Db())
        {
            var stored = await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == atTheEnd.Id);
            Assert.NotNull(stored.PublishedAt);
            Assert.Equal((stored.PublishedAt, (DateTime?)null), (stored.EarlyAccessFrom, stored.EarlyAccessFreedAt));
            Assert.Equal(new Listed(true, stored.PublishedAt!.Value.AddDays(7)), (await ReaderList(shelf, null))[atTheEnd.Id]);
        }

        // A draft moved among the first ten, then published, stays free; the chapters it moved past keep what they had.
        List<Guid> order =
        [
            .. Positions(1, 3).Select(p => shelf.At(p).Id), earlier.Id, .. Positions(4, 12).Select(p => shelf.At(p).Id), atTheEnd.Id
        ];
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, $"/api/myworks/{shelf.Novel.Id}/chapters", shelf.Author,
            JsonContent.Create(new { orderedChapterIds = order }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, shelf.Chapter(earlier), shelf.Author,
            JsonContent.Create(new { status = ChapterStatuses.Published }))).StatusCode);
        var locks = await StoredLocks(shelf);
        Assert.Equal(new Lock(null, null), locks[earlier.Id]);
        Assert.Equal(new Lock(null, null), locks[shelf.At(10).Id]); // now 11th, still free
        Assert.NotNull(locks[shelf.At(11).Id].From); // now 12th, still locked

        // Created published: it locks too, and the answer says so.
        var created = await (await api.Send(HttpMethod.Post, $"/api/novel/{shelf.Novel.Id}/chapter", shelf.Author,
            JsonContent.Create(new { title = "فصل جديد", content = "<p>نص</p>", status = ChapterStatuses.Published }))).OkJson();
        var publishedAt = created.GetProperty("publishedAt").GetDateTime();
        Assert.Equal((true, publishedAt.AddDays(7)), (created.GetProperty("isLocked").GetBoolean(), Date(created.GetProperty("unlocksAt"))));
        Assert.True((await ReaderList(shelf, null))[created.GetProperty("id").GetGuid()].IsLocked);
    }

    [Fact]
    public async Task Unpublishing_publishing_again_reordering_and_deleting_never_move_a_lock()
    {
        var shelf = await SeedNovel(published: 15);
        await Ok(await Enable(shelf, new { subscriptionCost = 200 }));
        var before = await StoredLocks(shelf);
        async Task Status(int position, string status) =>
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, shelf.Chapter(shelf.At(position)), shelf.Author,
                JsonContent.Create(new { status }))).StatusCode);

        // A free chapter unpublished: the first locked one stays locked; published again, it stays free.
        await Status(5, ChapterStatuses.Draft);
        Assert.True((await ReaderList(shelf, null))[shelf.At(11).Id].IsLocked);
        await Status(5, ChapterStatuses.Published);
        var list = await ReaderList(shelf, null);
        Assert.False(list[shelf.At(5).Id].IsLocked);
        Assert.All(Positions(11, 15), p => Assert.True(list[shelf.At(p).Id].IsLocked));

        // A locked chapter unpublished and published again keeps its lock, not renewed.
        await Status(12, ChapterStatuses.Draft);
        await Status(12, ChapterStatuses.Published);
        Assert.True((await ReaderList(shelf, null))[shelf.At(12).Id].IsLocked);

        // Reordered: an old free chapter moved to the end stays free, a locked one moved to the front stays locked.
        List<Guid> order =
        [
            shelf.At(13).Id, shelf.At(1).Id, .. Positions(3, 12).Select(p => shelf.At(p).Id), shelf.At(14).Id, shelf.At(15).Id, shelf.At(2).Id
        ];
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, $"/api/myworks/{shelf.Novel.Id}/chapters", shelf.Author,
            JsonContent.Create(new { orderedChapterIds = order }))).StatusCode);
        list = await ReaderList(shelf, null);
        Assert.Equal((false, true), (list[shelf.At(2).Id].IsLocked, list[shelf.At(13).Id].IsLocked));

        // Deleted: the others keep their locks.
        Assert.True((await api.Send(HttpMethod.Delete, shelf.Chapter(shelf.At(1)), shelf.Author)).IsSuccessStatusCode);

        var after = await StoredLocks(shelf);
        Assert.Equal(before.Where(kv => kv.Key != shelf.At(1).Id).OrderBy(kv => kv.Key), after.OrderBy(kv => kv.Key));
    }

    // ===== Changing it =====

    [Fact]
    public async Task New_days_count_from_each_lock_and_a_lock_already_over_never_comes_back()
    {
        var shelf = await SeedNovel(published: 14);
        await Ok(await Enable(shelf, new { subscriptionCost = 200, earlyAccessDays = 7 }));
        var now = DateTime.UtcNow;
        var (over, ending) = (now.AddDays(-8), now.AddDays(-5));
        await LockedSince(shelf.At(11), over); // over under 7 days
        await LockedSince(shelf.At(12), ending); // ends in 2 days

        // 10 days: 12 now ends 10 days after its start; 11, over before the change, stays open.
        await Ok(await Change(shelf, new { earlyAccessDays = 10 }));
        var list = await ReaderList(shelf, null);
        Assert.Equal((new Listed(false, null), new Listed(true, ending.AddDays(10))), (list[shelf.At(11).Id], list[shelf.At(12).Id]));
        Assert.NotNull((await StoredLocks(shelf))[shelf.At(11).Id].FreedAt);

        // 3 days: 12 is over; 30 days later on doesn't lock it again.
        await Ok(await Change(shelf, new { earlyAccessDays = 3 }));
        Assert.False((await ReaderList(shelf, null))[shelf.At(12).Id].IsLocked);
        await Ok(await Change(shelf, new { earlyAccessDays = 30 }));
        list = await ReaderList(shelf, null);
        Assert.False(list[shelf.At(12).Id].IsLocked);
        Assert.True(list[shelf.At(13).Id].IsLocked);

        // Subscribers only: the chapters still locked stay locked, with no end.
        await Ok(await Change(shelf, new { subscribersOnly = true }));
        var info = await Privilege(shelf, null);
        Assert.Equal(((int?)null, true, 2, (DateTime?)null),
            (Number(info.GetProperty("earlyAccessDays")), info.GetProperty("subscribersOnly").GetBoolean(),
                info.GetProperty("lockedChaptersCount").GetInt32(), Date(info.GetProperty("nextUnlockAt"))));
        Assert.Equal(new Listed(true, null), (await ReaderList(shelf, null))[shelf.At(13).Id]);

        // Back to days: 7 unless given.
        await Ok(await Change(shelf, new { subscribersOnly = false }));
        Assert.Equal(7, (await Privilege(shelf, null)).GetProperty("earlyAccessDays").GetInt32());

        Assert.Equal("ValidationFailed", await Refused(await Change(shelf, new { earlyAccessDays = 5, subscribersOnly = true })));
        Assert.Equal("InvalidEarlyAccessDays", await Refused(await Change(shelf, new { earlyAccessDays = 31 })));
        Assert.Equal("NoChanges", await Refused(await Change(shelf, new { earlyAccessDays = 7 })));
        Assert.Equal("NoChanges", await Refused(await Change(shelf, new { newSubscriptionCost = 200 })));
        Assert.Equal("NoChanges", await Refused(await Change(shelf, new { })));
        Assert.Equal("NotOwner", await Refused(await Change(shelf, new { earlyAccessDays = 5 }, await api.SignUp())));
    }

    [Fact]
    public async Task Moving_the_first_locked_chapter_forward_frees_the_ones_before_it_never_back()
    {
        var shelf = await SeedNovel(published: 15);
        await Ok(await Enable(shelf, new { subscriptionCost = 200 }));

        await Ok(await Change(shelf, new { newPrivilegeStartSequence = 13 }));
        var locks = await StoredLocks(shelf);
        Assert.All(Positions(11, 12), p => Assert.NotNull(locks[shelf.At(p).Id].FreedAt));
        Assert.All(Positions(13, 15), p => Assert.Null(locks[shelf.At(p).Id].FreedAt));
        var info = await Privilege(shelf, null);
        Assert.Equal((13, 3), (info.GetProperty("privilegeStartSequence").GetInt32(), info.GetProperty("lockedChaptersCount").GetInt32()));

        Assert.Equal("PrivilegeStartCannotMoveBack", await Refused(await Change(shelf, new { newPrivilegeStartSequence = 12 })));
        Assert.Equal("NoChanges", await Refused(await Change(shelf, new { newPrivilegeStartSequence = 13 })));
        Assert.Equal("InvalidPrivilegeStart", await Refused(await Change(shelf, new { newPrivilegeStartSequence = 16 })));
        Assert.Equal("FirstChaptersMustStayFree", await Refused(await Change(shelf, new { newPrivilegeStartSequence = 9 })));

        await Ok(await Change(shelf, new { newSubscriptionCost = 500 }));
        Assert.Equal(500m, (await Privilege(shelf, null)).GetProperty("subscriptionCost").GetDecimal());

        // None locked: locking starts after the last chapter, as the website reads it.
        foreach (var position in Positions(13, 15))
        {
            await Ok(await Unlock(shelf, shelf.At(position)));
        }
        info = await Privilege(shelf, null);
        Assert.Equal((0, (DateTime?)null, 16), (info.GetProperty("lockedChaptersCount").GetInt32(), Date(info.GetProperty("nextUnlockAt")),
            info.GetProperty("privilegeStartSequence").GetInt32()));
    }

    [Fact]
    public async Task Manual_unlock_frees_that_chapter_only_and_for_good()
    {
        var shelf = await SeedNovel(published: 15);
        var other = await SeedNovel(published: 12);
        await Ok(await Enable(shelf, new { subscriptionCost = 200 }));

        var unlocked = await (await Unlock(shelf, shelf.At(13))).OkJson();
        Assert.Contains("4", unlocked.GetProperty("message").GetString());
        var list = await ReaderList(shelf, null);
        Assert.False(list[shelf.At(13).Id].IsLocked);
        Assert.All(new[] { 11, 12, 14, 15 }, p => Assert.True(list[shelf.At(p).Id].IsLocked));

        Assert.Equal("ChapterNotLocked", await Refused(await Unlock(shelf, shelf.At(13))));
        Assert.Equal("ChapterNotLocked", await Refused(await Unlock(shelf, shelf.At(5))));
        Assert.Equal("ChapterNotFound", await Refused(await Unlock(shelf, other.At(12))));
        Assert.Equal("NotOwner", await Refused(await Unlock(shelf, shelf.At(14), await api.SignUp())));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.Send(HttpMethod.Post, $"{shelf.Privilege}/manual-unlock/{shelf.At(14).Id}")).StatusCode);

        // No later change locks it again.
        await Ok(await Change(shelf, new { earlyAccessDays = 30 }));
        await Ok(await Change(shelf, new { subscribersOnly = true }));
        Assert.False((await ReaderList(shelf, null))[shelf.At(13).Id].IsLocked);

        Assert.Equal("PrivilegeNotEnabled", await Refused(await Unlock(other, other.At(12))));
    }

    [Fact]
    public async Task Turning_it_off_opens_everything_keeps_subscriptions_and_turning_it_on_locks_the_latest_again()
    {
        var shelf = await SeedNovel(published: 13, drafts: 1);
        await Ok(await Enable(shelf, new { subscriptionCost = 200 }));
        var subscriber = await Subscriber(shelf);

        await Ok(await Disable(shelf));
        Assert.Equal(["isEnabled"], (await Privilege(shelf, null)).EnumerateObject().Select(p => p.Name));
        Assert.All((await ReaderList(shelf, null)).Values, chapter => Assert.Equal(new Listed(false, null), chapter));
        Assert.False((await (await api.Get(shelf.Chapter(shelf.At(13)))).OkJson()).GetProperty("isLocked").GetBoolean());

        // While off, a chapter that comes out doesn't lock, and there is nothing to subscribe to.
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, shelf.Chapter(shelf.At(14)), shelf.Author,
            JsonContent.Create(new { status = ChapterStatuses.Published }))).StatusCode);
        Assert.Equal(new Lock(null, null), (await StoredLocks(shelf))[shelf.At(14).Id]);
        Assert.Equal("PrivilegeNotEnabled", await Refused(await Disable(shelf)));
        Assert.Equal("PrivilegeNotEnabled", await Refused(await Change(shelf, new { earlyAccessDays = 5 })));
        Assert.Equal("NotOwner", await Refused(await Disable(shelf, await api.SignUp())));

        // On again: the latest chapters lock from now (positions 11 to 14 of 14), and the subscriber is still subscribed.
        var before = DateTime.UtcNow;
        await Ok(await Enable(shelf, new { subscriptionCost = 250, subscribersOnly = true }));
        var locks = await StoredLocks(shelf);
        Assert.All(Positions(11, 14), p => Assert.True(locks[shelf.At(p).Id].From >= before));
        Assert.All(Positions(1, 10), p => Assert.Equal(new Lock(null, null), locks[shelf.At(p).Id]));
        var asSubscriber = await Privilege(shelf, subscriber);
        Assert.Equal((true, 4), (asSubscriber.GetProperty("isSubscribed").GetBoolean(), asSubscriber.GetProperty("lockedChaptersCount").GetInt32()));
        Assert.All((await ReaderList(shelf, subscriber)).Values, chapter => Assert.False(chapter.IsLocked));
        Assert.Equal(4, (await ReaderList(shelf, null)).Values.Count(chapter => chapter.IsLocked));
        Assert.Equal(1, (await Privilege(shelf, shelf.Author)).GetProperty("subscribersCount").GetInt32());

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Send(HttpMethod.Post, shelf.Privilege + "/disable")).StatusCode);
    }

    // ===== Who reads a locked chapter =====

    [Fact]
    public async Task Subscribers_and_the_author_read_locked_chapters_and_the_authors_lists_say_what_others_meet()
    {
        var shelf = await SeedNovel(published: 12);
        await using (var db = api.Db())
        {
            db.ChapterParagraphs.Add(new ChapterParagraph
            {
                Id = Guid.NewGuid(), ChapterId = shelf.At(12).Id, Content = "<p>نص مبكر</p>", ContentHash = Guid.NewGuid().ToString("N"),
                OrderIndex = 0
            });
            await db.SaveChangesAsync();
        }
        await Ok(await Enable(shelf, new { subscriptionCost = 200, earlyAccessDays = 7 }));
        var subscriber = await Subscriber(shelf);
        var from = (await StoredLocks(shelf))[shelf.At(12).Id].From!.Value;

        // The subscriber and the author: nothing locked, the text there.
        foreach (var reader in new[] { subscriber, shelf.Author })
        {
            Assert.All((await ReaderList(shelf, reader)).Values, chapter => Assert.Equal(new Listed(false, null), chapter));
            var open = await (await api.Get(shelf.Chapter(shelf.At(12)), reader)).OkJson();
            Assert.Equal((false, (DateTime?)null, 1), (open.GetProperty("isLocked").GetBoolean(), Date(open.GetProperty("unlocksAt")),
                open.GetProperty("paragraphs").GetArrayLength()));
        }

        // Anyone else: locked, without the text, and when it opens.
        var closed = await (await api.Get(shelf.Chapter(shelf.At(12)), await api.SignUp())).OkJson();
        Assert.Equal((true, from.AddDays(7), 0), (closed.GetProperty("isLocked").GetBoolean(), Date(closed.GetProperty("unlocksAt")),
            closed.GetProperty("paragraphs").GetArrayLength()));

        // The author's own lists say what non-subscribers meet.
        var authors = await AuthorList(shelf);
        Assert.All(Positions(1, 10), p => Assert.Equal(new Listed(false, null), authors[shelf.At(p).Id]));
        Assert.All(Positions(11, 12), p => Assert.Equal(new Listed(true, from.AddDays(7)), authors[shelf.At(p).Id]));
        var authorsChapter = await (await api.Get($"/api/myworks/{shelf.Novel.Id}/chapters/{shelf.At(12).Id}", shelf.Author)).OkJson();
        Assert.Equal((true, from.AddDays(7)), (authorsChapter.GetProperty("isLocked").GetBoolean(), Date(authorsChapter.GetProperty("unlocksAt"))));
    }

    [Fact]
    public async Task A_save_of_a_chapter_from_an_older_copy_never_writes_its_lock_back()
    {
        var shelf = await SeedNovel(published: 12);
        await Ok(await Enable(shelf, new { subscriptionCost = 200 }));
        var locked = (await StoredLocks(shelf))[shelf.At(12).Id];

        // Loaded locked; meanwhile the author frees it; then the older copy is saved, its lock changed even.
        await using var stale = api.Db();
        var repository = new ChaptersRepository(stale);
        var copy = (await repository.GetChapterById(shelf.At(12).Id))!;
        await Ok(await Unlock(shelf, shelf.At(12)));
        copy.Title = "عنوان جديد";
        copy.EarlyAccessFreedAt = null;
        copy.EarlyAccessFrom = DateTime.UtcNow.AddYears(1);
        Assert.True((await repository.UpdateChapter(copy)).Saved);

        await using var db = api.Db();
        var stored = await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == shelf.At(12).Id);
        Assert.Equal("عنوان جديد", stored.Title);
        Assert.Equal(locked.From, stored.EarlyAccessFrom);
        Assert.NotNull(stored.EarlyAccessFreedAt);
        Assert.False((await ReaderList(shelf, null))[shelf.At(12).Id].IsLocked);
    }
}
