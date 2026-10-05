using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Chapters.Commands.UpdateChapter;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #75, editing a chapter from two places: the chapter's <c>revision</c> (and <c>updatedAt</c>) in the author's chapter,
/// the author's chapter list and the created chapter; a save from an older copy (a stale <c>baseRevision</c>) refused
/// with 409 ChapterChanged and nothing saved, no check without it; a change of status alone with no title or text;
/// and <c>?dryRun=true</c>, which says what a save would delete and saves nothing.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ChapterRevisionHttpTests(SardApiFactory api)
{
    [Fact]
    public async Task Each_save_that_changes_the_title_or_text_moves_the_revision_and_a_status_change_does_not()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var created = await (await Create(author, novel, "<p>أول</p>")).OkJson();
        var chapterId = created.GetProperty("id").GetGuid();
        Assert.Equal(1, created.GetProperty("revision").GetInt32());
        var createdAt = UpdatedAt(created);
        await AssertRevision(author, novel, chapterId, 1, createdAt);

        // The text changes: 2.
        var saved = await (await Patch(author, novel, chapterId, new { title = "فصل", content = "<p>أول</p><p>ثانٍ</p>" })).OkJson();
        Assert.True(saved.GetProperty("success").GetBoolean());
        Assert.Equal("حُفظ الفصل", saved.GetProperty("message").GetString());
        Assert.Equal(2, saved.GetProperty("revision").GetInt32());
        var afterText = await AssertRevision(author, novel, chapterId, 2, after: createdAt);

        // The title alone changes: 3. A change of formatting or kind is a change of the text too: 4.
        Assert.Equal(3, await Saved(author, novel, chapterId, new { title = "عنوان جديد", content = "<p>أول</p><p>ثانٍ</p>" }));
        Assert.Equal(4, await Saved(author, novel, chapterId, new { title = "عنوان جديد", content = "<p><b>أول</b></p><p data-kind=\"center\">ثانٍ</p>" }));

        // The web resends the title and text unchanged with a change of status: the revision stays, the status and
        // updatedAt move. A change of status alone, without title or text, the same.
        Assert.Equal(4, await Saved(author, novel, chapterId,
            new { title = "عنوان جديد", content = "<p><strong>أول</strong></p><p data-kind=\"center\">ثانٍ</p>", status = ChapterStatuses.Draft }));
        Assert.Equal(4, await Saved(author, novel, chapterId, new { status = ChapterStatuses.Published }));
        var last = await AssertRevision(author, novel, chapterId, 4, after: afterText);

        await using var db = api.Db();
        var stored = await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == chapterId);
        Assert.Equal((4, ChapterStatuses.Published, "عنوان جديد"), (stored.Revision, stored.Status, stored.Title));
        Assert.Equal(last, DateTime.SpecifyKind(stored.UpdatedAt, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_save_from_an_older_copy_is_refused_and_saves_nothing()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);
        var chapterId = (await (await Create(author, novel, "<p>أول</p><p>ثانٍ</p>")).OkJson()).GetProperty("id").GetGuid();
        var paragraphs = await ParagraphIds(chapterId);
        await Comment(reader, paragraphs[1]);

        // The phone saves from revision 1: fine, 2.
        Assert.Equal(2, await Saved(author, novel, chapterId, new { title = "فصل", content = "<p>أول</p><p>ثانٍ</p><p>ثالث</p>", baseRevision = 1 }));
        var before = await Snapshot(chapterId);

        // The web, still on revision 1, would drop the second paragraph and its comment: refused, nothing saved.
        var refused = await Patch(author, novel, chapterId,
            new { title = "عنوان آخر", content = "<p>أول</p>", status = ChapterStatuses.Draft, baseRevision = 1 });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var error = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ChapterChanged", error.GetProperty("code").GetString());
        Assert.Equal(ChapterChangedException.ArabicMessage, error.GetProperty("message").GetString());
        Assert.Equal(2, error.GetProperty("revision").GetInt32());
        Assert.Equal(before, await Snapshot(chapterId));

        // A dry run checks the revision too.
        Assert.Equal(HttpStatusCode.Conflict,
            (await Patch(author, novel, chapterId, new { title = "فصل", content = "<p>أول</p>", baseRevision = 1 }, dryRun: true)).StatusCode);
        // A title alone from the older copy as well.
        Assert.Equal(HttpStatusCode.Conflict,
            (await Patch(author, novel, chapterId, new { title = "عنوان آخر", content = "", baseRevision = 1 })).StatusCode);
        Assert.Equal(before, await Snapshot(chapterId));

        // From the chapter's revision it saves; without baseRevision there is no check, as before.
        Assert.Equal(3, await Saved(author, novel, chapterId, new { title = "فصل", content = "<p>أول</p><p>ثانٍ</p><p>ثالث جديد</p>", baseRevision = 2 }));
        Assert.Equal(4, await Saved(author, novel, chapterId, new { title = "فصل", content = "<p>أول</p><p>ثانٍ</p>" }));

        // A change of status alone doesn't touch the text: its baseRevision isn't checked.
        Assert.Equal(4, await Saved(author, novel, chapterId, new { status = ChapterStatuses.Draft, baseRevision = 1 }));
        Assert.Single(await ParagraphComments(paragraphs[1]));
    }

    [Fact]
    public async Task Two_saves_from_the_same_copy_cannot_both_succeed()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var chapterId = (await (await Create(author, novel, "<p>نص</p>")).OkJson()).GetProperty("id").GetGuid();

        for (var round = 1; round <= 5; round++)
        {
            var answers = await Task.WhenAll(
                Patch(author, novel, chapterId, new { title = "فصل", content = $"<p>من الهاتف {round}</p>", baseRevision = round }),
                Patch(author, novel, chapterId, new { title = "فصل", content = $"<p>من الموقع {round}</p>", baseRevision = round }));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], answers.Select(a => a.StatusCode).Order());
            var winner = answers.Single(a => a.StatusCode == HttpStatusCode.OK);
            Assert.Equal(round + 1, (await winner.OkJson()).GetProperty("revision").GetInt32());
            var loser = JsonDocument.Parse(await answers.Single(a => a != winner).Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(round + 1, loser.GetProperty("revision").GetInt32());

            // The chapter is the winner's text, whole.
            var chapter = await (await api.Get($"/api/myworks/{novel.Id}/chapters/{chapterId}", author)).OkJson();
            Assert.Equal(round + 1, chapter.GetProperty("revision").GetInt32());
            var text = Assert.Single(chapter.GetProperty("paragraphs").EnumerateArray()).GetProperty("content").GetString();
            Assert.Contains(text, new[] { $"من الهاتف {round}", $"من الموقع {round}" });
        }
    }

    [Fact]
    public async Task A_change_of_status_alone_needs_no_title_or_text_and_the_limits_stay_when_they_are_sent()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var chapterId = (await (await Create(author, novel, "<p>نص</p>", ChapterStatuses.Draft)).OkJson()).GetProperty("id").GetGuid();

        Assert.Equal(1, await Saved(author, novel, chapterId, new { status = ChapterStatuses.Published }));
        var read = await (await api.Get($"/api/novel/{novel.Id}/chapter/{chapterId}?prefetch=true")).OkJson();
        Assert.Equal("نص", Assert.Single(read.GetProperty("paragraphs").EnumerateArray()).GetProperty("content").GetString());
        await using (var db = api.Db())
        {
            var stored = await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == chapterId);
            Assert.Equal(ChapterStatuses.Published, stored.Status);
            Assert.NotNull(stored.PublishedAt);
        }

        await AssertRefused(await Patch(author, novel, chapterId, new { }), UpdateChapterValidator.StatusMissingMessage);
        await AssertRefused(await Patch(author, novel, chapterId, new { title = "فصل" }), "اكتب نص الفصل");
        await AssertRefused(await Patch(author, novel, chapterId, new { content = "<p>نص</p>" }), "اكتب عنوان الفصل");
        await AssertRefused(await Patch(author, novel, chapterId, new { title = new string('ع', 51), content = "<p>نص</p>" }),
            "يجب ألا يتجاوز عنوان الفصل 50 حرفًا");
        await AssertRefused(await Patch(author, novel, chapterId, new { title = "فصل", content = "<p>" + new string('ن', 100_001) + "</p>" }),
            "يجب ألا يتجاوز نص الفصل 100000 حرف");
        await AssertRefused(await Patch(author, novel, chapterId, new { status = "NotAStatus" }), "حالة الفصل يجب أن تكون «مسودة» أو «منشور»");
    }

    [Fact]
    public async Task A_dry_run_says_what_a_save_would_delete_and_saves_nothing()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var other = await api.SignUp();
        var novel = await api.AddNovel(author);
        const string content = "<p>الأولى</p><p>الثانية</p><p>الثالثة</p><p>الرابعة</p>";
        var chapterId = (await (await Create(author, novel, content)).OkJson()).GetProperty("id").GetGuid();
        var paragraphs = await ParagraphIds(chapterId);

        // The second paragraph: a comment with a reply, and a comment its author deleted. The third: one comment.
        var onSecond = await Comment(reader, paragraphs[1]);
        await Comment(other, paragraphs[1], parent: onSecond);
        var deleted = await Comment(other, paragraphs[1]);
        (await api.Send(HttpMethod.Delete, $"/api/comment/{deleted}", other)).EnsureSuccessStatusCode();
        await Comment(reader, paragraphs[2]);
        var before = await Snapshot(chapterId);

        // Without the second paragraph, the third one rewritten and the fourth in bold (kept).
        var edited = new { title = "فصل", content = "<p>الأولى</p><p>الثالثة بعد التعديل</p><p><b>الرابعة</b></p>", baseRevision = 1 };
        var preview = await (await Patch(author, novel, chapterId, edited, dryRun: true)).OkJson();

        Assert.Equal(2, preview.GetProperty("paragraphsRemoved").GetInt32());
        Assert.Equal(3, preview.GetProperty("commentsDeleted").GetInt32());
        Assert.Equal([(paragraphs[1], 2), (paragraphs[2], 1)], preview.GetProperty("removed").EnumerateArray()
            .Select(r => (r.GetProperty("paragraphId").GetGuid(), r.GetProperty("commentsCount").GetInt32())));
        Assert.Equal(["paragraphsRemoved", "commentsDeleted", "removed"], preview.EnumerateObject().Select(p => p.Name));
        Assert.Equal(before, await Snapshot(chapterId));

        // A save with nothing to delete says so.
        var harmless = await (await Patch(author, novel, chapterId, new { title = "فصل", content = content + "<p>جديدة</p>" }, dryRun: true)).OkJson();
        Assert.Equal((0, 0, 0), (harmless.GetProperty("paragraphsRemoved").GetInt32(), harmless.GetProperty("commentsDeleted").GetInt32(),
            harmless.GetProperty("removed").GetArrayLength()));
        var statusOnly = await (await Patch(author, novel, chapterId, new { status = ChapterStatuses.Draft }, dryRun: true)).OkJson();
        Assert.Equal(0, statusOnly.GetProperty("paragraphsRemoved").GetInt32());
        Assert.Equal(before, await Snapshot(chapterId));

        // The save itself deletes what the dry run said.
        var visibleBefore = await VisibleComments(chapterId);
        Assert.Equal(2, await Saved(author, novel, chapterId, edited));
        Assert.Equal(visibleBefore - 3, await VisibleComments(chapterId));
        var kept = await ParagraphIds(chapterId);
        Assert.Equal([paragraphs[0], paragraphs[3]], new[] { kept[0], kept[2] });
        Assert.DoesNotContain(paragraphs[1], kept);
        Assert.DoesNotContain(paragraphs[2], kept);
    }

    // ---- Helpers ----

    /// <summary>The comments on the chapter's paragraphs that readers see, replies included.</summary>
    private async Task<int> VisibleComments(Guid chapterId)
    {
        await using var db = api.Db();
        return await db.Comments.CountAsync(c =>
            c.ParagraphId != null && db.ChapterParagraphs.Any(p => p.Id == c.ParagraphId && p.ChapterId == chapterId));
    }

    private Task<HttpResponseMessage> Create(ApiUser author, Novel novel, string content, string status = ChapterStatuses.Published) =>
        api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status, title = "فصل", content }));

    private Task<HttpResponseMessage> Patch(ApiUser author, Novel novel, Guid chapterId, object body, bool dryRun = false) =>
        api.Send(HttpMethod.Patch, $"/api/novel/{novel.Id}/chapter/{chapterId}" + (dryRun ? "?dryRun=true" : ""), author,
            JsonContent.Create(body));

    /// <summary>Saves and answers the revision the save gave.</summary>
    private async Task<int> Saved(ApiUser author, Novel novel, Guid chapterId, object body) =>
        (await (await Patch(author, novel, chapterId, body)).OkJson()).GetProperty("revision").GetInt32();

    /// <summary>
    /// The revision in the author's chapter and chapter list, and their updatedAt (UTC with "Z"), equal to
    /// <paramref name="updatedAt"/> or later than <paramref name="after"/>; answers it.
    /// </summary>
    private async Task<DateTime> AssertRevision(ApiUser author, Novel novel, Guid chapterId, int revision,
        DateTime? updatedAt = null, DateTime? after = null)
    {
        var chapter = await (await api.Get($"/api/myworks/{novel.Id}/chapters/{chapterId}", author)).OkJson();
        var listed = (await (await api.Get($"/api/myworks/{novel.Id}/chapters", author)).OkJson()).EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == chapterId);
        Assert.Equal((revision, revision), (chapter.GetProperty("revision").GetInt32(), listed.GetProperty("revision").GetInt32()));
        var at = UpdatedAt(chapter);
        Assert.Equal(at, UpdatedAt(listed));
        if (updatedAt is { } exactly)
        {
            Assert.Equal(exactly, at);
        }

        if (after is { } earlier)
        {
            Assert.True(at > earlier, $"{at:O} after {earlier:O}");
        }

        // Readers' list has neither.
        var readers = (await (await api.Get($"/api/novel/{novel.Id}/chapter")).OkJson()).EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("id").GetGuid() == chapterId);
        if (readers.ValueKind == JsonValueKind.Object)
        {
            Assert.False(readers.TryGetProperty("revision", out _));
        }

        return at;
    }

    private static DateTime UpdatedAt(JsonElement chapter)
    {
        var text = chapter.GetProperty("updatedAt").GetString()!;
        Assert.EndsWith("Z", text);
        return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
    }

    private async Task<Guid> Comment(ApiUser user, Guid paragraphId, Guid? parent = null)
    {
        var fields = parent is { } p
            ? new[] { ("Content", "رد"), ("ParentCommentId", p.ToString()) }
            : new[] { ("Content", "تعليق") };
        var body = await (await api.Send(HttpMethod.Post, $"/api/comment/paragraph/{paragraphId}", user, ReaderApi.Form(fields))).OkJson();
        return body.GetProperty("comment").GetProperty("id").GetGuid();
    }

    private async Task<List<Guid>> ParagraphIds(Guid chapterId)
    {
        await using var db = api.Db();
        return await db.ChapterParagraphs.Where(p => p.ChapterId == chapterId).OrderBy(p => p.OrderIndex).Select(p => p.Id).ToListAsync();
    }

    private async Task<List<JsonElement>> ParagraphComments(Guid paragraphId) =>
        (await (await api.Get($"/api/comment/paragraph/{paragraphId}")).OkJson()).GetProperty("items").EnumerateArray().ToList();

    /// <summary>Everything a save could change: the chapter's row, its paragraphs, and the comments on them.</summary>
    private async Task<List<string>> Snapshot(Guid chapterId)
    {
        await using var db = api.Db();
        var chapter = await db.Chapters.AsNoTracking().Where(c => c.Id == chapterId)
            .Select(c => $"{c.Title}|{c.Slug}|{c.Status}|{c.Revision}|{c.UpdatedAt:O}|{c.ParagraphsCount}|{c.TotalCommentsCount}")
            .SingleAsync();
        var paragraphs = await db.ChapterParagraphs.AsNoTracking().Where(p => p.ChapterId == chapterId).OrderBy(p => p.OrderIndex)
            .Select(p => $"{p.Id}|{p.OrderIndex}|{p.ContentType}|{p.Content}|{p.ContentHash}|{p.CommentsCount}|{p.UpdatedAt}")
            .ToListAsync();
        var comments = await db.Comments.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.ParagraphId != null && db.ChapterParagraphs.Any(p => p.Id == c.ParagraphId && p.ChapterId == chapterId))
            .OrderBy(c => c.Id).Select(c => $"{c.Id}|{c.IsDeleted}").ToListAsync();
        return [chapter, .. paragraphs, .. comments];
    }

    private static async Task AssertRefused(HttpResponseMessage response, string message)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{(int)response.StatusCode}: {body}");
        var error = JsonDocument.Parse(body).RootElement;
        Assert.Equal("ValidationFailed", error.GetProperty("code").GetString());
        Assert.Equal(message, error.GetProperty("message").GetString());
    }
}
