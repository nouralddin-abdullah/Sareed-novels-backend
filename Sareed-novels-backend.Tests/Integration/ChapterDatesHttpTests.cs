using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #39: the chapter payloads say when each chapter came out, <c>publishedAt</c> (UTC with "Z", null for a draft never
/// published), next to <c>createdAt</c> where they have it, which stays as it was: the novel's chapter list
/// (GET /api/novel/{id}/chapter), the author's (GET /api/myworks/{id}/chapters), the reader's chapter
/// (GET /api/novel/{id}/chapter/{chapterId}), and the author's chapter and the chapter she creates.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ChapterDatesHttpTests(SardApiFactory api)
{
    [Fact]
    public async Task Every_chapter_payload_says_when_the_chapter_came_out()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var createdPublished = await (await Create(author, novel, ChapterStatuses.Published)).OkJson();
        var publishedLater = Id(await (await Create(author, novel, ChapterStatuses.Draft)).OkJson());
        var neverPublished = await (await Create(author, novel, ChapterStatuses.Draft)).OkJson();
        await Save(author, novel, publishedLater, ChapterStatuses.Published);
        var (createdId, draftId) = (Id(createdPublished), Id(neverPublished));
        var stored = await Stored(createdId, publishedLater, draftId);

        // The chapter she creates: published, it came out then; a draft hasn't.
        Assert.Equal(stored[createdId].PublishedAt, PublishedAt(createdPublished));
        Assert.Null(PublishedAt(neverPublished));

        // Readers' list: the published chapters. The draft published later came out after it was written.
        var listed = await (await api.Get($"/api/novel/{novel.Id}/chapter")).OkJson();
        Assert.Equal([createdId, publishedLater], Ids(listed));
        var list = Items(listed);
        Assert.Equal(stored[createdId].PublishedAt, PublishedAt(list[createdId]));
        Assert.Equal(stored[publishedLater].PublishedAt, PublishedAt(list[publishedLater]));
        Assert.True(PublishedAt(list[publishedLater]) > CreatedAt(list[publishedLater]));

        // The author's list: every chapter, the draft with null.
        var mineListed = await (await api.Get($"/api/myworks/{novel.Id}/chapters", author)).OkJson();
        Assert.Equal([createdId, publishedLater, draftId], Ids(mineListed));
        var mine = Items(mineListed);
        Assert.Equal(stored[publishedLater].PublishedAt, PublishedAt(mine[publishedLater]));
        Assert.Equal(JsonValueKind.Null, mine[draftId].GetProperty("publishedAt").ValueKind);

        // The reader's chapter, and the author's.
        var read = await (await api.Get($"/api/novel/{novel.Id}/chapter/{publishedLater}")).OkJson();
        Assert.Equal(stored[publishedLater].PublishedAt, PublishedAt(read));
        var edited = await (await api.Get($"/api/myworks/{novel.Id}/chapters/{draftId}", author)).OkJson();
        Assert.Null(PublishedAt(edited));

        // Unpublished and published again: it keeps when it first came out.
        await Save(author, novel, publishedLater, ChapterStatuses.Draft);
        Assert.Equal(stored[publishedLater].PublishedAt, PublishedAt(Items(
            await (await api.Get($"/api/myworks/{novel.Id}/chapters", author)).OkJson())[publishedLater]));
        await Save(author, novel, publishedLater, ChapterStatuses.Published);
        read = await (await api.Get($"/api/novel/{novel.Id}/chapter/{publishedLater}")).OkJson();
        Assert.Equal(stored[publishedLater].PublishedAt, PublishedAt(read));
    }

    private Task<HttpResponseMessage> Create(ApiUser author, Novel novel, string status) =>
        api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status, title = "فصل " + Seed.Marker(), content = "<p>نص</p>" }));

    /// <summary>The author saves a chapter with this status, sending its title and text as the editor does.</summary>
    private async Task Save(ApiUser author, Novel novel, Guid chapterId, string status) =>
        (await api.Send(HttpMethod.Patch, $"/api/novel/{novel.Id}/chapter/{chapterId}", author,
            JsonContent.Create(new { status, title = "فصل", content = "<p>نص</p>" }))).EnsureSuccessStatusCode();

    private async Task<Dictionary<Guid, (DateTime CreatedAt, DateTime? PublishedAt)>> Stored(params Guid[] chapterIds)
    {
        await using var db = api.Db();
        return await db.Chapters
            .Where(c => chapterIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.CreatedAt, c.PublishedAt));
    }

    private static Guid Id(JsonElement chapter) => chapter.GetProperty("id").GetGuid();

    private static List<Guid> Ids(JsonElement list) => list.EnumerateArray().Select(Id).ToList();

    private static Dictionary<Guid, JsonElement> Items(JsonElement list) => list.EnumerateArray().ToDictionary(Id);

    /// <summary><c>publishedAt</c>, which must be there: UTC with "Z", or null.</summary>
    private static DateTime? PublishedAt(JsonElement chapter)
    {
        var value = chapter.GetProperty("publishedAt");
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        var text = value.GetString()!;
        Assert.EndsWith("Z", text);
        return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
    }

    /// <summary><c>createdAt</c>, as it always was: UTC without the "Z".</summary>
    private static DateTime CreatedAt(JsonElement chapter) =>
        DateTime.Parse(chapter.GetProperty("createdAt").GetString()!, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
