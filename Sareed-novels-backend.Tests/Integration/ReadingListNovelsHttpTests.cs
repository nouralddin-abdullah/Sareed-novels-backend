using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sareed_novels_backend.Middlewares;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The novels on reading lists (#35): which of my lists have a novel (GET my-lists?containsNovelId=, for the app's
/// «أضف إلى قائمة» sheet), and the owner putting them in a new order (PATCH {id}/novels/order).
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ReadingListNovelsHttpTests(SardApiFactory api)
{
    private async Task Add(ApiUser owner, Guid list, Guid novel) =>
        (await api.Send(HttpMethod.Post, $"/api/readinglist/{list}/novels/{novel}", owner)).EnsureSuccessStatusCode();

    private Task<HttpResponseMessage> Reorder(ApiUser? user, Guid list, IEnumerable<Guid> orderedNovelIds) =>
        api.Send(HttpMethod.Patch, $"/api/readinglist/{list}/novels/order", user, JsonContent.Create(new { orderedNovelIds }));

    /// <summary>The novels of the list's page, in the order it shows them, with their orderIndex.</summary>
    private async Task<List<(Guid Id, int OrderIndex)>> Novels(ApiUser user, Guid list) =>
        (await (await api.Get($"/api/readinglist/{list}", user)).OkJson()).GetProperty("novels").EnumerateArray()
            .Select(n => (n.GetProperty("novelId").GetGuid(), n.GetProperty("orderIndex").GetInt32()))
            .ToList();

    private async Task<List<Guid>> NovelIds(ApiUser user, Guid list) => (await Novels(user, list)).Select(n => n.Id).ToList();

    /// <summary>A list of <paramref name="count"/> new published novels, added in order.</summary>
    private async Task<(Guid List, List<Guid> Novels)> ListWithNovels(ApiUser owner, int count)
    {
        var author = await api.SignUp();
        var list = await api.ReadingList(owner);
        var novels = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var novel = await api.AddNovel(author);
            await Add(owner, list, novel.Id);
            novels.Add(novel.Id);
        }
        return (list, novels);
    }

    private async Task<List<JsonElement>> Items(ApiUser user, string url) =>
        (await (await api.Get(url, user)).OkJson()).GetProperty("items").EnumerateArray().ToList();

    /// <summary>containsNovel of each of the user's lists, by list id; null where the field is left out.</summary>
    private async Task<Dictionary<Guid, bool?>> ContainsNovel(ApiUser user, string query)
    {
        var items = await Items(user, $"/api/readinglist/my-lists?pageSize=100{query}");
        return items.ToDictionary(
            item => item.GetProperty("id").GetGuid(),
            item => item.TryGetProperty("containsNovel", out var value) ? value.GetBoolean() : (bool?)null);
    }

    [Fact]
    public async Task containsNovel_is_true_for_the_lists_that_have_the_novel_and_absent_without_the_parameter()
    {
        var (author, reader, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var another = await api.AddNovel(author);
        var withIt = await api.ReadingList(reader);
        var privateWithIt = await api.ReadingList(reader, isPublic: false);
        var withAnother = await api.ReadingList(reader);
        var empty = await api.ReadingList(reader);
        await Add(reader, withIt, novel.Id);
        await Add(reader, withIt, another.Id);
        await Add(reader, privateWithIt, novel.Id);
        await Add(reader, withAnother, another.Id);
        // Someone else's list with the novel is not the reader's.
        var othersList = await api.ReadingList(other);
        await Add(other, othersList, novel.Id);

        var asked = await ContainsNovel(reader, $"&containsNovelId={novel.Id}");
        Assert.Equal(new Dictionary<Guid, bool?> { [withIt] = true, [privateWithIt] = true, [withAnother] = false, [empty] = false }, asked);

        // Without the parameter the field isn't there, here or on the other list pages.
        Assert.All((await ContainsNovel(reader, "")).Values, Assert.Null);
        Assert.All(await Items(reader, $"/api/readinglist/user/{other.UserName}"), item => Assert.False(item.TryGetProperty("containsNovel", out _)));
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/readinglist/{othersList}/follow", reader)).StatusCode);
        Assert.All(await Items(reader, "/api/readinglist/followed"), item => Assert.False(item.TryGetProperty("containsNovel", out _)));

        // Paged: each page says it for its own lists.
        var firstPage = await Items(reader, $"/api/readinglist/my-lists?pageSize=2&pageNumber=1&containsNovelId={novel.Id}");
        var secondPage = await Items(reader, $"/api/readinglist/my-lists?pageSize=2&pageNumber=2&containsNovelId={novel.Id}");
        Assert.Equal(asked, firstPage.Concat(secondPage).ToDictionary(i => i.GetProperty("id").GetGuid(), i => (bool?)i.GetProperty("containsNovel").GetBoolean()));

        // Removed from a list, the list says false; a novel nobody has is on none.
        (await api.Send(HttpMethod.Delete, $"/api/readinglist/{withIt}/novels/{novel.Id}", reader)).EnsureSuccessStatusCode();
        Assert.False((await ContainsNovel(reader, $"&containsNovelId={novel.Id}"))[withIt]);
        Assert.All((await ContainsNovel(reader, $"&containsNovelId={Guid.NewGuid()}")).Values, value => Assert.False(value));
    }

    [Fact]
    public async Task containsNovel_agrees_with_adding_and_removing_even_for_a_novel_hidden_since()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var list = await api.ReadingList(reader);
        await Add(reader, list, novel.Id);
        await using (var db = api.Db())
        {
            await db.Novels.Where(n => n.Id == novel.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDraft, true));
        }

        // The list still has it (hidden from readers), and removing it from there works.
        Assert.True((await ContainsNovel(reader, $"&containsNovelId={novel.Id}"))[list]);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, $"/api/readinglist/{list}/novels/{novel.Id}", reader)).StatusCode);
        Assert.False((await ContainsNovel(reader, $"&containsNovelId={novel.Id}"))[list]);
    }

    [Fact]
    public async Task containsNovelId_that_is_not_an_id_is_refused()
    {
        var reader = await api.SignUp();

        var body = await (await api.Get("/api/readinglist/my-lists?containsNovelId=abc", reader)).Error(HttpStatusCode.BadRequest);

        Assert.Equal("ValidationFailed", body.GetProperty("code").GetString());
        Assert.Matches(@"\p{IsArabic}", body.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task The_owner_puts_the_novels_in_a_new_order()
    {
        var owner = await api.SignUp();
        var (list, novels) = await ListWithNovels(owner, 4);
        var (a, b, c, d) = (novels[0], novels[1], novels[2], novels[3]);
        Assert.Equal([a, b, c, d], await NovelIds(owner, list));
        var updatedBefore = (await (await api.Get($"/api/readinglist/{list}", owner)).OkJson()).GetProperty("updatedAt").GetDateTime();

        var body = await (await Reorder(owner, list, [c, a, d, b])).OkJson();

        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Matches(@"\p{IsArabic}", body.GetProperty("message").GetString()!);
        Assert.Equal([(c, 0), (a, 1), (d, 2), (b, 3)], await Novels(owner, list));
        var card = (await Items(owner, "/api/readinglist/my-lists?pageSize=100")).Single(i => i.GetProperty("id").GetGuid() == list);
        Assert.Equal([c, a, d, b], card.GetProperty("previewNovels").EnumerateArray().Select(n => n.GetProperty("novelId").GetGuid()));
        Assert.True(card.GetProperty("updatedAt").GetDateTime() > updatedBefore);

        // The same order again changes nothing and is fine; a novel added afterwards goes to the end.
        (await Reorder(owner, list, [c, a, d, b])).EnsureSuccessStatusCode();
        var e = (await api.AddNovel(await api.SignUp())).Id;
        await Add(owner, list, e);
        Assert.Equal([c, a, d, b, e], await NovelIds(owner, list));
        (await Reorder(owner, list, [e, d, c, b, a])).EnsureSuccessStatusCode();
        Assert.Equal([(e, 0), (d, 1), (c, 2), (b, 3), (a, 4)], await Novels(owner, list));
    }

    [Fact]
    public async Task Only_the_owner_reorders()
    {
        var (owner, other) = (await api.SignUp(), await api.SignUp());
        var (list, novels) = await ListWithNovels(owner, 3);
        var reversed = Enumerable.Reverse(novels).ToList();

        var notOwner = await (await Reorder(other, list, reversed)).Error(HttpStatusCode.Forbidden);
        Assert.Equal("NotOwner", notOwner.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Reorder(null, list, reversed)).StatusCode);
        var missing = await (await Reorder(owner, Guid.NewGuid(), reversed)).Error(HttpStatusCode.NotFound);
        Assert.Equal("ReadingListNotFound", missing.GetProperty("code").GetString());

        Assert.Equal(novels, await NovelIds(owner, list));
    }

    [Fact]
    public async Task The_ids_must_be_exactly_the_lists_novels()
    {
        var owner = await api.SignUp();
        var (list, novels) = await ListWithNovels(owner, 3);
        var (a, b, c) = (novels[0], novels[1], novels[2]);
        var notInList = (await api.AddNovel(await api.SignUp())).Id;
        var before = await Novels(owner, list);

        List<Guid>[] refused =
        [
            [c, b],                    // one missing
            [c, b, a, notInList],      // one extra, a novel of another list
            [c, b, Guid.NewGuid()],    // one replaced by a novel that doesn't exist
            [c, b, b],                 // a repeat in place of a
            [c, b, a, a],              // a repeat on top
            []
        ];
        foreach (var ids in refused)
        {
            var body = await (await Reorder(owner, list, ids)).Error(HttpStatusCode.BadRequest);
            Assert.Equal("NovelOrderMismatch", body.GetProperty("code").GetString());
            Assert.Matches(@"\p{IsArabic}", body.GetProperty("message").GetString()!);
            Assert.False(body.GetProperty("success").GetBoolean());
        }

        // No ids at all, or an empty body: the request itself is refused.
        foreach (var content in new HttpContent[]
                 {
                     JsonContent.Create(new { }), JsonContent.Create(new { orderedNovelIds = (Guid[]?)null }),
                     new StringContent("", Encoding.UTF8, "application/json")
                 })
        {
            var body = await (await api.Send(HttpMethod.Patch, $"/api/readinglist/{list}/novels/order", owner, content)).Error(HttpStatusCode.BadRequest);
            Assert.Equal(ValidationProblems.Code, body.GetProperty("code").GetString());
            Assert.Matches(@"\p{IsArabic}", body.GetProperty("message").GetString()!);
        }

        Assert.Equal(before, await Novels(owner, list));

        // An empty list's novels are none.
        var empty = await api.ReadingList(owner);
        (await Reorder(owner, empty, [])).EnsureSuccessStatusCode();
        Assert.Equal("NovelOrderMismatch", (await (await Reorder(owner, empty, [a])).Error(HttpStatusCode.BadRequest)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Novels_readers_cannot_open_keep_their_place()
    {
        var owner = await api.SignUp();
        var (list, novels) = await ListWithNovels(owner, 4);
        var (a, hidden, b, c) = (novels[0], novels[1], novels[2], novels[3]);
        async Task SetDraft(bool isDraft)
        {
            await using var db = api.Db();
            await db.Novels.Where(n => n.Id == hidden).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDraft, isDraft));
        }
        await SetDraft(true);
        Assert.Equal([a, b, c], await NovelIds(owner, list));

        // The list shows three novels: those are the ones to send, and the hidden one isn't one of them.
        Assert.Equal("NovelOrderMismatch", (await (await Reorder(owner, list, [c, hidden, b, a])).Error(HttpStatusCode.BadRequest))
            .GetProperty("code").GetString());
        (await Reorder(owner, list, [c, b, a])).EnsureSuccessStatusCode();
        Assert.Equal([c, b, a], await NovelIds(owner, list));

        // Published again, it is back in its place, second.
        await SetDraft(false);
        Assert.Equal([(c, 0), (hidden, 1), (b, 2), (a, 3)], await Novels(owner, list));
    }

    [Fact]
    public async Task Concurrent_reorders_each_apply_whole()
    {
        var owner = await api.SignUp();
        var (list, novels) = await ListWithNovels(owner, 6);
        var orders = Enumerable.Range(0, 8).Select(i => novels.OrderBy(_ => Random.Shared.Next()).ToList()).ToList();

        var responses = await Task.WhenAll(orders.Select(order => Reorder(owner, list, order)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var final = await Novels(owner, list);
        Assert.Contains(orders, order => order.SequenceEqual(final.Select(n => n.Id)));
        Assert.Equal(Enumerable.Range(0, 6), final.Select(n => n.OrderIndex));
    }
}
