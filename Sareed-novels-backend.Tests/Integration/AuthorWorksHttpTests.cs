using System.Data.SqlTypes;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// GET /api/myworks/user/{userId}, a member's public works, the same for every caller. With withChapters=true (#46, the
/// app's «أعمال أخرى للكاتب» shelf) only the novels a reader can open, with at least one published chapter, and paging
/// that counts only those. Without it (the web's profiles) every public novel, as before. Drafts and deleted novels are
/// never listed.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class AuthorWorksHttpTests(SardApiFactory api)
{
    private const string WithChapters = "&withChapters=true";

    /// <summary>A page of <paramref name="author"/>'s works as <paramref name="viewer"/> gets it (anonymously when null).</summary>
    private async Task<JsonElement> Works(ApiUser author, ApiUser? viewer, string flag, int pageNumber = 1, int pageSize = 50) =>
        await (await api.Get($"/api/myworks/user/{author.Id}?pageNumber={pageNumber}&pageSize={pageSize}{flag}", viewer)).OkJson();

    private static List<Guid> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("id").GetGuid()).ToList();

    /// <summary>A chapter the author writes in the editor (POST /api/novel/{id}/chapter), published or as a draft.</summary>
    private async Task<Guid> Write(ApiUser author, Novel novel, string status) =>
        (await (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status, title = "فصل " + Seed.Marker(), content = "<p>نص</p>" }))).OkJson())
        .GetProperty("id").GetGuid();

    /// <summary>
    /// An author with a public novel a reader can open, three public novels with nothing to read yet (no chapter; drafts
    /// only, one of them published and made a draft again; its only published chapter deleted), and a draft and a
    /// deleted novel that each have a published chapter.
    /// </summary>
    private async Task<(ApiUser Author, Novel Readable, Novel[] Unreadable)> AuthorWithWorks()
    {
        var author = await api.SignUp();

        var readable = await api.AddNovel(author);
        await Write(author, readable, ChapterStatuses.Published);

        var noChapters = await api.AddNovel(author);

        var draftsOnly = await api.AddNovel(author);
        await Write(author, draftsOnly, ChapterStatuses.Draft);
        var unpublished = await Write(author, draftsOnly, ChapterStatuses.Published);
        (await api.Send(HttpMethod.Patch, $"/api/novel/{draftsOnly.Id}/chapter/{unpublished}", author,
            JsonContent.Create(new { status = ChapterStatuses.Draft, title = "فصل", content = "<p>نص</p>" }))).EnsureSuccessStatusCode();

        var chapterDeleted = await api.AddNovel(author);
        var onlyChapter = await Write(author, chapterDeleted, ChapterStatuses.Published);
        (await api.Send(HttpMethod.Delete, $"/api/novel/{chapterDeleted.Id}/chapter/{onlyChapter}", author)).EnsureSuccessStatusCode();

        var draft = await api.AddNovel(author, isDraft: true);
        await api.AddChapter(draft, "<p>فقرة</p>");
        var deleted = await api.AddNovel(author);
        await api.AddChapter(deleted, "<p>فقرة</p>");
        (await api.Send(HttpMethod.Delete, $"/api/myworks/{deleted.Id}/delete", author)).EnsureSuccessStatusCode();

        return (author, readable, [noChapters, draftsOnly, chapterDeleted]);
    }

    [Fact]
    public async Task With_chapters_everyone_the_author_too_gets_only_the_novels_a_reader_can_open()
    {
        var (author, readable, _) = await AuthorWithWorks();
        var member = await api.SignUp();

        foreach (var viewer in new[] { null, member, author })
        {
            var page = await Works(author, viewer, WithChapters);
            Assert.Equal(new[] { readable.Id }, Ids(page));
            Assert.Equal(1, page.GetProperty("totalItemsCount").GetInt32());
            Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
        }
    }

    [Fact]
    public async Task Without_it_everyone_gets_every_public_novel_as_before()
    {
        var (author, readable, unreadable) = await AuthorWithWorks();
        var member = await api.SignUp();
        var everyPublicNovel = unreadable.Append(readable).Select(n => n.Id).Order().ToList();

        foreach (var viewer in new[] { null, member, author })
        {
            foreach (var flag in new[] { "", "&withChapters=false" })
            {
                var page = await Works(author, viewer, flag);
                Assert.Equal(everyPublicNovel, Ids(page).Order());
                Assert.Equal(4, page.GetProperty("totalItemsCount").GetInt32());
                Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
            }
        }
    }

    [Fact]
    public async Task Bearer_undefined_is_answered_as_anonymous()
    {
        var author = await api.SignUp();
        var readable = await api.AddNovel(author);
        await api.AddChapter(readable, "<p>فقرة</p>");
        var noChapters = await api.AddNovel(author);

        // The web sends its sign-in cookie's token with the request, "Bearer undefined" when signed out.
        foreach (var (flag, expected) in new[] { ("", new[] { readable.Id, noChapters.Id }), (WithChapters, new[] { readable.Id }) })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/myworks/user/{author.Id}?pageSize=50{flag}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "undefined");
            var page = await (await api.Client().SendAsync(request)).OkJson();

            Assert.Equal(expected.Order(), Ids(page).Order());
            Assert.Equal(expected.Length, page.GetProperty("totalItemsCount").GetInt32());
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1")]
    public async Task A_with_chapters_value_other_than_true_or_false_is_refused(string value)
    {
        var author = await api.SignUp();
        await api.AddNovel(author);

        var body = await (await api.Get($"/api/myworks/user/{author.Id}?withChapters={value}")).Error(HttpStatusCode.BadRequest);

        Assert.Equal("ValidationFailed", body.GetProperty("code").GetString());
        Assert.Matches(@"\p{IsArabic}", body.GetProperty("message").GetString()!);
        Assert.True(body.GetProperty("errors").TryGetProperty("withChapters", out _), body.ToString());
    }

    [Fact]
    public async Task Paging_counts_and_pages_exactly_what_is_listed()
    {
        var author = await api.SignUp();
        var member = await api.SignUp();

        // Twelve public novels, updated a minute apart, newest first. The 3rd, 7th and 11th have nothing to read (no
        // chapter, drafts only, a chapter published and made a draft again), so pages cut before leaving them out would
        // come up short.
        var novels = new List<Novel>();
        for (var i = 0; i < 12; i++)
        {
            novels.Add(await api.AddNovel(author));
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

        // Newest update first, then SQL Server's order of ids.
        List<Guid> InListOrder(IEnumerable<Novel> listed) =>
            listed.Select(n => n.Id).OrderByDescending(id => updatedAt[id]).ThenBy(id => new SqlGuid(id)).ToList();

        foreach (var viewer in new[] { null, member, author })
        {
            await AssertPagesOfFive(author, viewer, WithChapters, InListOrder(novels.Except(unreadable))); // 9: pages of 5 and 4
            await AssertPagesOfFive(author, viewer, "", InListOrder(novels)); // 12: pages of 5, 5 and 2
        }
    }

    /// <summary>
    /// Each page of 5 of the author's works that <paramref name="viewer"/> gets holds exactly the next 5 of
    /// <paramref name="expected"/>, and says how many there are in all; the page after the last is empty.
    /// </summary>
    private async Task AssertPagesOfFive(ApiUser author, ApiUser? viewer, string flag, List<Guid> expected)
    {
        const int size = 5;
        var totalPages = (expected.Count + size - 1) / size;
        for (var number = 1; number <= totalPages + 1; number++)
        {
            var page = await Works(author, viewer, flag, number, size);
            Assert.Equal(expected.Skip((number - 1) * size).Take(size), Ids(page));
            Assert.Equal(expected.Count, page.GetProperty("totalItemsCount").GetInt32());
            Assert.Equal(totalPages, page.GetProperty("totalPages").GetInt32());
            if (number <= totalPages)
            {
                Assert.Equal(Math.Min(number * size, expected.Count), page.GetProperty("itemsTo").GetInt32());
            }
        }
    }
}
