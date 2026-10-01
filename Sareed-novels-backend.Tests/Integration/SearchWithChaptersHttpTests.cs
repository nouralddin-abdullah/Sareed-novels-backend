using System.Data.SqlTypes;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Application.Search.DTOs;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Sareed_novels_backend.Middlewares;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// GET and POST /api/search/novels with withChapters=true (#58, the app's «آخر التحديثات»): only the novels a reader can
/// open, with at least one published chapter, as a member's works with the same flag (#46), in the page and in the
/// counts. Without it (or false) the search lists every novel that isn't a draft, those without a published chapter
/// too, as the owner decided. Drafts and deleted novels are never listed. A value that isn't a boolean is refused.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class SearchWithChaptersHttpTests(SardApiFactory api)
{
    public enum Via { Get, Post }

    /// <summary>The novels of one test: their titles share a marker, and they share a genre.</summary>
    private sealed record Shelf(string Marker, Genre Genre);

    private async Task<Shelf> NewShelf()
    {
        var genre = Seed.Genre();
        await using var db = api.Db();
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        return new Shelf(Seed.Marker(), genre);
    }

    /// <summary>A novel on the shelf (or a draft), saved directly.</summary>
    private async Task<Novel> AddNovel(ApiUser author, Shelf shelf, bool isDraft = false)
    {
        var novel = await api.AddNovel(author, isDraft, title: $"رواية {shelf.Marker} {Guid.NewGuid().ToString("N")[..6]}");
        await using var db = api.Db();
        db.NovelGenres.Add(new NovelGenre { NovelId = novel.Id, GenreId = shelf.Genre.Id });
        await db.SaveChangesAsync();
        return novel;
    }

    /// <summary>A chapter the author writes in the editor (POST /api/novel/{id}/chapter), published or as a draft.</summary>
    private async Task<Guid> Write(ApiUser author, Novel novel, string status) =>
        (await (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status, title = "فصل " + Seed.Marker(), content = "<p>نص</p>" }))).OkJson())
        .GetProperty("id").GetGuid();

    /// <summary>
    /// The shelf's novels as the search lists them, latest update first, asked the way the apps ask: GET with query
    /// parameters or POST with a JSON body, browsing the shelf's genre without a query or searching for its marker.
    /// <paramref name="withChapters"/> goes as written (a query value, or a JSON value), and is left out when null.
    /// </summary>
    private Task<HttpResponseMessage> Search(Via via, Shelf shelf, bool browse, string? withChapters, int pageNumber = 1, int pageSize = 50)
    {
        if (via == Via.Get)
        {
            var scope = browse ? $"genres={Uri.EscapeDataString(shelf.Genre.Slug)}" : $"query={Uri.EscapeDataString(shelf.Marker)}";
            var flag = withChapters is null ? "" : $"&withChapters={withChapters}";
            return api.Get($"/api/search/novels?{scope}&sortBy={NovelSortBy.LastUpdated}&pageNumber={pageNumber}&pageSize={pageSize}{flag}");
        }

        var fields = new List<string>
        {
            browse ? $"\"genres\":[{JsonSerializer.Serialize(shelf.Genre.Slug)}]" : $"\"query\":{JsonSerializer.Serialize(shelf.Marker)}",
            $"\"sortBy\":{(int)NovelSortBy.LastUpdated}",
            $"\"pageNumber\":{pageNumber}",
            $"\"pageSize\":{pageSize}"
        };
        if (withChapters is not null)
        {
            fields.Add($"\"withChapters\":{withChapters}");
        }
        var body = new StringContent("{" + string.Join(',', fields) + "}", Encoding.UTF8, "application/json");
        return api.Send(HttpMethod.Post, "/api/search/novels", content: body);
    }

    /// <summary>
    /// A shelf with a public novel a reader can open, three public novels with nothing to read yet (no chapter; drafts
    /// only, one of them published and made a draft again; its only published chapter deleted), written through the API
    /// as the editor does, and a draft and a deleted novel that each have a published chapter.
    /// </summary>
    private async Task<(Shelf Shelf, Novel Readable, Novel[] Unreadable)> ShelfOfEveryKind()
    {
        var author = await api.SignUp();
        var shelf = await NewShelf();

        var readable = await AddNovel(author, shelf);
        await Write(author, readable, ChapterStatuses.Published);

        var noChapters = await AddNovel(author, shelf);

        var draftsOnly = await AddNovel(author, shelf);
        await Write(author, draftsOnly, ChapterStatuses.Draft);
        var unpublished = await Write(author, draftsOnly, ChapterStatuses.Published);
        (await api.Send(HttpMethod.Patch, $"/api/novel/{draftsOnly.Id}/chapter/{unpublished}", author,
            JsonContent.Create(new { status = ChapterStatuses.Draft, title = "فصل", content = "<p>نص</p>" }))).EnsureSuccessStatusCode();

        var chapterDeleted = await AddNovel(author, shelf);
        var onlyChapter = await Write(author, chapterDeleted, ChapterStatuses.Published);
        (await api.Send(HttpMethod.Delete, $"/api/novel/{chapterDeleted.Id}/chapter/{onlyChapter}", author)).EnsureSuccessStatusCode();

        var draft = await AddNovel(author, shelf, isDraft: true);
        await api.AddChapter(draft, "<p>فقرة</p>");
        var deleted = await AddNovel(author, shelf);
        await api.AddChapter(deleted, "<p>فقرة</p>");
        (await api.Send(HttpMethod.Delete, $"/api/myworks/{deleted.Id}/delete", author)).EnsureSuccessStatusCode();

        return (shelf, readable, [noChapters, draftsOnly, chapterDeleted]);
    }

    [Theory]
    [InlineData(Via.Get)]
    [InlineData(Via.Post)]
    public async Task With_chapters_only_the_novels_a_reader_can_open_are_listed_and_counted(Via via)
    {
        var (shelf, readable, _) = await ShelfOfEveryKind();

        foreach (var browse in new[] { true, false })
        {
            var page = await (await Search(via, shelf, browse, "true")).OkJson();
            Assert.Equal([readable.Id], page.Ids());
            Assert.Equal(1, page.GetProperty("totalItemsCount").GetInt32());
            Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
            Assert.Equal(1, page.GetProperty("items")[0].GetProperty("chapterCount").GetInt32());
        }
    }

    [Theory]
    [InlineData(Via.Get)]
    [InlineData(Via.Post)]
    public async Task Without_it_every_novel_that_is_not_a_draft_is_listed_as_before(Via via)
    {
        var (shelf, readable, unreadable) = await ShelfOfEveryKind();
        var everyPublicNovel = unreadable.Append(readable).Select(n => n.Id).Order().ToList();

        // Left out, false, or empty: GET withChapters= and a JSON null are read as left out.
        foreach (var withChapters in new[] { null, "false", via == Via.Get ? "" : "null" })
        {
            foreach (var browse in new[] { true, false })
            {
                var page = await (await Search(via, shelf, browse, withChapters)).OkJson();
                Assert.Equal(everyPublicNovel, page.Ids().Order());
                Assert.Equal(4, page.GetProperty("totalItemsCount").GetInt32());
                Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
            }
        }
    }

    [Theory]
    [InlineData(Via.Get)]
    [InlineData(Via.Post)]
    public async Task Paging_counts_and_pages_exactly_what_is_listed(Via via)
    {
        var author = await api.SignUp();
        var shelf = await NewShelf();

        // Twelve novels, updated a minute apart, newest first. The 3rd, 7th and 11th have nothing to read (no chapter,
        // drafts only, a chapter published and made a draft again), so pages cut before leaving them out would come up
        // short.
        var novels = new List<Novel>();
        for (var i = 0; i < 12; i++)
        {
            novels.Add(await AddNovel(author, shelf));
        }
        Novel[] unreadable = [novels[2], novels[6], novels[10]];

        var start = DateTime.UtcNow.AddDays(-1);
        var updatedAt = novels.Select((n, i) => (n.Id, At: start.AddMinutes(-i))).ToDictionary(x => x.Id, x => x.At);
        // The 5th to 8th updated at the same moment, across the end of the first page of both lists: ties go by id, so
        // pages neither repeat nor skip one.
        foreach (var novel in novels[5..8])
        {
            updatedAt[novel.Id] = updatedAt[novels[4].Id];
        }

        await using (var db = api.Db())
        {
            foreach (var novel in novels.Except(unreadable))
            {
                db.Chapters.AddRange(Seed.Chapters(novel, 1, start));
            }
            db.Chapters.AddRange(Seed.Chapters(novels[6], 2, start, status: ChapterStatuses.Draft));
            var unpublished = Seed.Chapters(novels[10], 1, start).Single();
            unpublished.Status = ChapterStatuses.Draft;
            db.Chapters.Add(unpublished);
            await db.SaveChangesAsync();

            foreach (var (id, at) in updatedAt)
            {
                await db.Novels.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.LastUpdatedAt, at));
            }
        }

        // Latest update first, then SQL Server's order of ids.
        List<Guid> InListOrder(IEnumerable<Novel> listed) =>
            listed.Select(n => n.Id).OrderByDescending(id => updatedAt[id]).ThenBy(id => new SqlGuid(id)).ToList();

        foreach (var browse in new[] { true, false })
        {
            await AssertPagesOfFive(via, shelf, browse, "true", InListOrder(novels.Except(unreadable))); // 9: pages of 5 and 4
            await AssertPagesOfFive(via, shelf, browse, null, InListOrder(novels)); // 12: pages of 5, 5 and 2
        }
    }

    /// <summary>
    /// Each page of 5 holds exactly the next 5 of <paramref name="expected"/>, and says how many there are in all; the
    /// page after the last is empty.
    /// </summary>
    private async Task AssertPagesOfFive(Via via, Shelf shelf, bool browse, string? withChapters, List<Guid> expected)
    {
        const int size = 5;
        var totalPages = (expected.Count + size - 1) / size;
        for (var number = 1; number <= totalPages + 1; number++)
        {
            var page = await (await Search(via, shelf, browse, withChapters, number, size)).OkJson();
            Assert.Equal(expected.Skip((number - 1) * size).Take(size), page.Ids());
            Assert.Equal(expected.Count, page.GetProperty("totalItemsCount").GetInt32());
            Assert.Equal(totalPages, page.GetProperty("totalPages").GetInt32());
            if (number <= totalPages)
            {
                Assert.Equal(Math.Min(number * size, expected.Count), page.GetProperty("itemsTo").GetInt32());
            }
        }
    }

    [Theory]
    [InlineData(Via.Get, "abc")]
    [InlineData(Via.Get, "1")]
    [InlineData(Via.Post, "\"abc\"")]
    [InlineData(Via.Post, "1")]
    [InlineData(Via.Post, "\"true\"")]
    public async Task A_with_chapters_value_that_is_not_a_boolean_is_refused(Via via, string value)
    {
        var author = await api.SignUp();
        var shelf = await NewShelf();
        await AddNovel(author, shelf);

        var body = await (await Search(via, shelf, browse: true, value)).Error(HttpStatusCode.BadRequest);

        // As any value of the wrong type: in a query, under the parameter's name, as #46 answers; in a JSON body, under
        // its JSON path, as every body the API reads.
        Assert.Equal(ValidationProblems.Code, body.GetProperty("code").GetString());
        var (field, error, message) = via == Via.Get
            ? ("withChapters", ValidationProblems.InvalidValueMessage, ValidationProblems.Title)
            : ("$.withChapters", ValidationProblems.UnreadableBodyMessage, ValidationProblems.UnreadableBodyMessage);
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out var errors), body.ToString());
        Assert.Equal([error], errors.EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(message, body.GetProperty("message").GetString());
    }
}
