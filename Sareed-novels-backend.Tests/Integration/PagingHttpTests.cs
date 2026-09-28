using System.Net;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Lists that didn't clamp their paging: page 0 or a negative page reached EF as a negative Skip (a 500, or a 403
/// through the error middleware), and a size of 0 or less returned every row. Now pages start at 1 and hold 1..50.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class PagingHttpTests(SardApiFactory api)
{
    private static readonly (string Query, int PageNumber, int PageSize)[] OutOfRange =
    [
        ("pageNumber=0&pageSize=0", 1, 1),
        ("pageNumber=-3&pageSize=-7", 1, 1),
        ("pageNumber=1&pageSize=100000", 1, 50),
    ];

    private const string HugePage = "pageNumber=2147483647&pageSize=50";

    private static List<JsonElement> Items(JsonElement page, string list = "items") => page.GetProperty(list).EnumerateArray().ToList();

    [Fact]
    public async Task Followers_and_following_never_return_every_row()
    {
        var star = await api.SignUp();
        var fans = new[] { await api.SignUp(), await api.SignUp(), await api.SignUp() };
        foreach (var fan in fans)
        {
            (await api.Follow(fan, star)).EnsureSuccessStatusCode();
            (await api.Follow(star, fan)).EnsureSuccessStatusCode();
        }

        foreach (var list in new[] { "followers-list", "following-list" })
        {
            foreach (var (query, _, size) in OutOfRange)
            {
                var page = await (await api.Get($"/api/User/{list}/{star.Id}?{query}")).OkJson();
                Assert.Equal(Math.Min(size, 3), Items(page).Count);
                Assert.Equal(3, page.GetProperty("totalItemsCount").GetInt32());
                Assert.Equal(Math.Min(size, 3), page.GetProperty("itemsTo").GetInt32());
            }

            var huge = await (await api.Get($"/api/User/{list}/{star.Id}?{HugePage}")).OkJson();
            Assert.Empty(Items(huge));
        }
    }

    [Fact]
    public async Task Reviews_never_return_every_review()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        await api.Review(await api.SignUp(), novel.Id);
        await api.Review(await api.SignUp(), novel.Id);

        foreach (var (query, number, size) in OutOfRange)
        {
            var page = await (await api.Get($"/api/{novel.Id}?{query}")).OkJson();
            Assert.Equal(Math.Min(size, 2), Items(page, "reviews").Count);
            Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(number, page.GetProperty("currentPage").GetInt32());
            Assert.Equal(size, page.GetProperty("pageSize").GetInt32());
        }
        Assert.Empty(Items(await (await api.Get($"/api/{novel.Id}?{HugePage}")).OkJson(), "reviews"));
    }

    [Fact]
    public async Task Wiki_entities_accept_any_page_and_up_to_100_a_page()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        await using (var db = api.Db())
        {
            db.NovelEntities.AddRange(Enumerable.Range(1, 3).Select(i => new NovelEntity
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, Section = "شخصيات", Name = $"شخصية {i}"
            }));
            await db.SaveChangesAsync();
        }

        foreach (var query in new[] { "pageNumber=0&pageSize=0", "pageNumber=-3&pageSize=-7" })
        {
            var page = await (await api.Get($"/api/novels/{novel.Id}/entities?{query}")).OkJson();
            Assert.Single(Items(page));
            Assert.Equal(3, page.GetProperty("totalItemsCount").GetInt32());
        }

        // The SEO worker lists a novel's wiki with pageSize=100.
        var all = await (await api.Get($"/api/novels/{novel.Id}/entities?pageNumber=1&pageSize=100000")).OkJson();
        Assert.Equal(3, Items(all).Count);
        Assert.Equal(1, all.GetProperty("totalPages").GetInt32());
        Assert.Empty(Items(await (await api.Get($"/api/novels/{novel.Id}/entities?{HugePage}")).OkJson()));
    }

    [Fact]
    public async Task Competition_novels_and_leaderboard_accept_any_page_and_top()
    {
        var competition = new Competition
        {
            Id = Guid.NewGuid(), Name = "مسابقة", Slug = "c-" + Seed.Marker(), Status = CompetitionStatus.Participation,
            ParticipationStartDate = DateTime.UtcNow.AddDays(-1), ParticipationEndDate = DateTime.UtcNow.AddDays(10),
            JudgmentStartDate = DateTime.UtcNow.AddDays(11), JudgmentEndDate = DateTime.UtcNow.AddDays(12), ResultsDate = DateTime.UtcNow.AddDays(13)
        };
        var author = await api.SignUp();
        var novels = new[] { await api.AddNovel(author), await api.AddNovel(author) };
        await using (var db = api.Db())
        {
            db.Competitions.Add(competition);
            db.CompetitionParticipants.AddRange(novels.Select((n, i) => new CompetitionParticipant
            {
                Id = Guid.NewGuid(), CompetitionId = competition.Id, NovelId = n.Id, CurrentPoints = i
            }));
            await db.SaveChangesAsync();
        }

        foreach (var sortBy in new[] { "top", "newest" })
        {
            foreach (var (query, _, size) in OutOfRange)
            {
                var page = await (await api.Get($"/api/competition/{competition.Id}/novels?sortBy={sortBy}&{query}")).OkJson();
                Assert.Equal(Math.Min(size, 2), Items(page).Count);
                Assert.Equal(2, page.GetProperty("totalItemsCount").GetInt32());
            }
        }

        foreach (var (top, expected) in new[] { (0, 1), (-4, 1), (100000, 2) })
        {
            var leaders = await (await api.Get($"/api/competition/{competition.Id}/leaderboard?top={top}")).OkJson();
            Assert.Equal(expected, leaders.GetArrayLength());
        }
    }

    [Fact]
    public async Task My_subscriptions_notifications_and_wallet_lists_accept_any_page()
    {
        var user = await api.SignUp();

        foreach (var (query, number, size) in OutOfRange)
        {
            var subscriptions = await (await api.Get($"/api/privilege/my-subscriptions?{query}", user)).OkJson();
            Assert.Equal(number, subscriptions.GetProperty("pageNumber").GetInt32());
            Assert.Equal(size, subscriptions.GetProperty("pageSize").GetInt32());
            Assert.Equal(0, subscriptions.GetProperty("totalPages").GetInt32());

            var notifications = await (await api.Get($"/api/notifications?{query}", user)).OkJson();
            Assert.Equal(number, notifications.GetProperty("pageNumber").GetInt32());
            Assert.Equal(size, notifications.GetProperty("pageSize").GetInt32());

            foreach (var wallet in new[] { "transactions", "recharge", "withdraw" })
            {
                var response = await api.Get($"/api/wallet/{wallet}?{query}", user);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        foreach (var list in new[] { "privilege/my-subscriptions", "notifications", "wallet/transactions", "gift/my-history" })
        {
            Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/{list}?{HugePage}", user)).StatusCode);
        }
    }

    [Fact]
    public async Task Admin_request_queues_accept_any_page()
    {
        var admin = await api.SignUpAdmin();

        foreach (var queue in new[] { "recharge/pending", "withdraw/pending" })
        {
            foreach (var (query, _, _) in OutOfRange)
            {
                Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/admin/{queue}?{query}", admin)).StatusCode);
            }
            Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/admin/{queue}?{HugePage}", admin)).StatusCode);
        }
    }

    [Fact]
    public async Task Works_lists_accept_any_page()
    {
        var author = await api.SignUp();
        await api.AddNovel(author);
        await api.AddNovel(author);

        foreach (var (query, _, size) in OutOfRange)
        {
            var mine = await (await api.Get($"/api/myworks?{query}", author)).OkJson();
            Assert.Equal(size == 50 ? 2 : 1, Items(mine).Count);
            var theirs = await (await api.Get($"/api/myworks/user/{author.Id}?{query}")).OkJson();
            Assert.Equal(size == 50 ? 2 : 1, Items(theirs).Count);
        }
    }
}
