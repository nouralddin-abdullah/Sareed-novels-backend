using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Follows, list follows and a list's novels under concurrent requests (a double tap, an app retrying): one of them
/// changes the state, the others find it done, and none fails (two follows of one person at once used to be a 500).
/// A write whose state is already as asked answers 204 No Content (#25).
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class IdempotentWritesHttpTests(SardApiFactory api)
{
    private const int AtOnce = 8;

    /// <summary>One request did it (200 with its result); the others found it done (204, no body).</summary>
    private static async Task AssertDoneOrAlreadyDone(HttpResponseMessage[] responses)
    {
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var response in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }
    }

    private static Task<HttpResponseMessage[]> AtOnceDo(Func<Task<HttpResponseMessage>> request) =>
        Task.WhenAll(Enumerable.Range(0, AtOnce).Select(_ => Task.Run(request)));

    [Fact]
    public async Task Following_and_unfollowing_a_member_at_once_changes_it_once()
    {
        var (me, them) = (await api.SignUp(), await api.SignUp());

        await AssertDoneOrAlreadyDone(await AtOnceDo(() => api.Follow(me, them)));
        await using (var db = api.Db())
        {
            Assert.Equal(1, await db.Follows.CountAsync(f => f.FollowerId == me.Id && f.FollowedId == them.Id));
        }

        await AssertDoneOrAlreadyDone(await AtOnceDo(() => api.Send(HttpMethod.Delete, "/api/User/unfollow", me,
            JsonContent.Create(new { userToUnFollowId = them.Id }))));
        await using (var db = api.Db())
        {
            Assert.False(await db.Follows.AnyAsync(f => f.FollowerId == me.Id && f.FollowedId == them.Id));
        }
    }

    [Fact]
    public async Task Following_a_list_and_adding_or_removing_a_novel_at_once_counts_once()
    {
        var (owner, reader) = (await api.SignUp(), await api.SignUp());
        var list = await api.ReadingList(owner);
        var novel = await api.AddNovel(owner);

        await AssertDoneOrAlreadyDone(await AtOnceDo(() => api.Send(HttpMethod.Post, $"/api/readinglist/{list}/follow", reader)));
        // Adding a novel twice is still refused (400 AlreadyInList): only these writes' repeats are 204.
        var adds = await AtOnceDo(() => api.Send(HttpMethod.Post, $"/api/readinglist/{list}/novels/{novel.Id}", owner));
        Assert.Single(adds, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var refused in adds.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            Assert.Equal("AlreadyInList", (await refused.Error(HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        }
        await using (var db = api.Db())
        {
            var row = await db.ReadingLists.SingleAsync(l => l.Id == list);
            Assert.Equal((1, 1), (row.FollowersCount, row.NovelsCount));
            Assert.Equal(1, await db.ReadingListFollowers.CountAsync(f => f.ReadingListId == list));
        }

        await AssertDoneOrAlreadyDone(await AtOnceDo(() => api.Send(HttpMethod.Delete, $"/api/readinglist/{list}/unfollow", reader)));
        // Removing a novel answers 204 either way.
        Assert.All(await AtOnceDo(() => api.Send(HttpMethod.Delete, $"/api/readinglist/{list}/novels/{novel.Id}", owner)),
            r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        await using (var db = api.Db())
        {
            var row = await db.ReadingLists.SingleAsync(l => l.Id == list);
            Assert.Equal((0, 0), (row.FollowersCount, row.NovelsCount));
            Assert.False(await db.ReadingListNovels.AnyAsync(n => n.ReadingListId == list));
        }
    }
}
