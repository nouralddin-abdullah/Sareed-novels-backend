using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The novels on reading lists (#35): which of my lists have a novel (GET my-lists?containsNovelId=, for the app's
/// «أضف إلى قائمة» sheet).
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ReadingListNovelsHttpTests(SardApiFactory api)
{
    private async Task Add(ApiUser owner, Guid list, Guid novel) =>
        (await api.Send(HttpMethod.Post, $"/api/readinglist/{list}/novels/{novel}", owner)).EnsureSuccessStatusCode();

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
}
