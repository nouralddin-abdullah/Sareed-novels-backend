using System.Data.SqlTypes;
using System.Net;
using System.Net.Http.Json;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #80, the writer studio's follow-ups, through the API: a hidden novel's chapters coming out tell no reader (and
/// publishing the novel later doesn't either), the novel page tells its author the novel is hidden (<c>isDraft</c>),
/// <c>GET /api/myworks</c> pages in a stable order, and deleting a chapter that isn't there is 404 <c>ChapterNotFound</c>.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class WriterStudioFollowupsHttpTests(SardApiFactory api)
{
    [Fact]
    public async Task A_hidden_novels_chapters_tell_no_reader_and_publishing_the_novel_later_doesnt_either()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var (first, _) = await api.AddChapter(novel, "<p>الفصل الأول</p>");
        var reader = await api.SignUp();
        await InLibrary(reader, first);
        (await api.Send(HttpMethod.Patch, $"/api/myworks/{novel.Id}/draft", author)).EnsureSuccessStatusCode();

        // Created published, and a draft published, while the novel is hidden.
        var created = await Create(author, novel, ChapterStatuses.Published);
        var draft = await Create(author, novel, ChapterStatuses.Draft);
        (await api.Send(HttpMethod.Patch, $"/api/novel/{novel.Id}/chapter/{draft}", author,
            JsonContent.Create(new { status = ChapterStatuses.Published }))).EnsureSuccessStatusCode();
        await AssertNotAnnounced(reader, created, draft);

        // Published, the novel's next chapter is announced; those that came out while it was hidden aren't.
        (await api.Send(HttpMethod.Patch, $"/api/myworks/{novel.Id}/publish", author)).EnsureSuccessStatusCode();
        var next = await Create(author, novel, ChapterStatuses.Published);
        Assert.Equal(1, await Announcements(reader, next));
        await AssertNotAnnounced(reader, created, draft);
    }

    [Fact]
    public async Task The_novel_page_tells_its_author_that_her_novel_is_hidden()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var hidden = await api.AddNovel(author, isDraft: true);
        var shown = await api.AddNovel(author);

        Assert.True(await IsDraft(ReaderApi.BySlug(hidden.Slug), author));
        Assert.True(await IsDraft($"/api/novel/by-id/{hidden.Id}", author));
        Assert.False(await IsDraft(ReaderApi.BySlug(shown.Slug), author));
        Assert.False(await IsDraft(ReaderApi.BySlug(shown.Slug), reader));
        Assert.False(await IsDraft($"/api/novel/by-id/{shown.Id}", null));

        // Readers never get a hidden novel's page.
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get(ReaderApi.BySlug(hidden.Slug), reader)).StatusCode);
    }

    [Fact]
    public async Task My_works_pages_list_every_work_once_in_a_stable_order()
    {
        var author = await api.SignUp();
        var tied = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            tied.Add((await api.AddNovel(author)).Id);
        }
        // Updated at the same moment: only the id tells them apart.
        await using (var db = api.Db())
        {
            var at = DateTime.UtcNow.AddDays(-1);
            await db.Novels.Where(n => tied.Contains(n.Id)).ExecuteUpdateAsync(s => s.SetProperty(n => n.LastUpdatedAt, at));
        }
        var latest = await api.AddNovel(author);

        var listed = new List<Guid>();
        for (var page = 1; page <= 5; page++)
        {
            var body = await (await api.Get($"/api/myworks?pageNumber={page}&pageSize=2", author)).OkJson();
            Assert.Equal(8, body.GetProperty("totalItemsCount").GetInt32());
            listed.AddRange(body.GetProperty("items").EnumerateArray().Select(w => w.GetProperty("id").GetGuid()));
        }

        // Last updated first, then by id, as SQL Server orders ids; every work once.
        Assert.Equal([latest.Id, .. tied.OrderBy(id => new SqlGuid(id))], listed);
    }

    [Fact]
    public async Task Deleting_a_chapter_that_isnt_there_is_404_ChapterNotFound_in_arabic()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var other = await api.AddNovel(author);
        var (elsewhere, _) = await api.AddChapter(other, "<p>نص</p>");

        foreach (var chapterId in new[] { Guid.NewGuid(), elsewhere.Id })
        {
            var error = await (await api.Send(HttpMethod.Delete, $"/api/novel/{novel.Id}/chapter/{chapterId}", author))
                .Error(HttpStatusCode.NotFound);
            Assert.Equal(("ChapterNotFound", "الفصل غير موجود"),
                (error.GetProperty("code").GetString(), error.GetProperty("message").GetString()));
        }

        await using var db = api.Db();
        Assert.True(await db.Chapters.AnyAsync(c => c.Id == elsewhere.Id));
    }

    private async Task<Guid> Create(ApiUser author, Novel novel, string status)
    {
        var created = await (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status, title = "فصل " + Seed.Marker(), content = "<p>نص الفصل</p>" }))).OkJson();
        return created.GetProperty("id").GetGuid();
    }

    private async Task<bool> IsDraft(string url, ApiUser? user) =>
        (await (await api.Get(url, user)).OkJson()).GetProperty("isDraft").GetBoolean();

    private async Task InLibrary(ApiUser reader, Chapter chapter)
    {
        await using var db = api.Db();
        db.UserNovelProgress.Add(Seed.Progress(new User { Id = reader.Id }, chapter, 1, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<int> CountAnnouncements(ApiUser reader, Guid chapterId)
    {
        await using var db = api.Db();
        return await db.Notifications.CountAsync(n =>
            n.UserId == reader.Id && n.RelatedEntityId == chapterId && n.Type == NotificationType.NewChapterInLibrary);
    }

    /// <summary>
    /// The reader's new-chapter notifications for this chapter, sent in the background after the publish: this waits for
    /// one (up to 10 s), then a moment more, so a second would show.
    /// </summary>
    private async Task<int> Announcements(ApiUser reader, Guid chapterId)
    {
        for (var waited = 0; waited < 100 && await CountAnnouncements(reader, chapterId) == 0; waited++)
        {
            await Task.Delay(100);
        }
        await Task.Delay(300);
        return await CountAnnouncements(reader, chapterId);
    }

    /// <summary>No notification for these chapters, a moment after their publish (one would be sent in the background).</summary>
    private async Task AssertNotAnnounced(ApiUser reader, params Guid[] chapterIds)
    {
        await Task.Delay(1500);
        foreach (var chapterId in chapterIds)
        {
            Assert.Equal(0, await CountAnnouncements(reader, chapterId));
        }
    }
}
