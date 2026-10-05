using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Chapters;
using Application.Chapters.Paragraphs;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Chapter format v1 (#74) through the API: whatever the author's editor sends, the chapter is stored and read back as
/// paragraphs with a kind and inline content (POST /api/novel/{novelId}/chapter, PATCH .../chapter/{chapterId}, the
/// author's GET /api/myworks/{workId}/chapters/{chapterId}, the reader's GET .../chapter/{chapterId}); a change of kind
/// or formatting alone keeps a paragraph and its comments; the 100,000 limit counts visible text; and paragraphs stored
/// before the format leave the API clean everywhere chapter text does.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ChapterFormatHttpTests(SardApiFactory api)
{
    /// <summary>One paragraph of each kind, written as a client might, with markup the format doesn't keep.</summary>
    private const string EveryKind =
        "<p class=\"min-h-[1em]\" style=\"color:red\">كان <b>الليل</b> طويلاً<br>والريح <i>تعوي</i>.</p>" +
        "<p data-kind=\"center\">بيتٌ من الشعر<br>وبيتٌ آخر</p>" +
        "<p data-kind=\"quote\" onclick=\"alert(1)\">«عزيزتي نور، <u>لا تنسي</u>»<script>alert(1)</script></p>" +
        "<hr>" +
        "<p data-kind=\"image\"><img src=\"https://files.test/map.png\" onerror=\"alert(1)\">خريطة <strong>المدينة</strong></p>" +
        "<p data-kind=\"break\"></p>" +
        "<p data-kind=\"unknown\"><a href=\"javascript:alert(1)\">النهاية</a> &lt;تمت&gt;</p>" +
        "<p class=\"min-h-[1em]\"></p>";

    private static readonly (string Kind, string Content, string? Caption)[] EveryKindStored =
    [
        (ParagraphKinds.Text, "كان <strong>الليل</strong> طويلاً<br>والريح <em>تعوي</em>.", null),
        (ParagraphKinds.Center, "بيتٌ من الشعر<br>وبيتٌ آخر", null),
        (ParagraphKinds.Quote, "«عزيزتي نور، <u>لا تنسي</u>»", null),
        (ParagraphKinds.Break, "* * *", null),
        (ParagraphKinds.Image, "https://files.test/map.png", "خريطة المدينة"),
        (ParagraphKinds.Break, "* * *", null),
        (ParagraphKinds.Text, "النهاية &lt;تمت&gt;", null)
    ];

    [Fact]
    public async Task A_chapter_is_stored_and_read_back_in_the_format_with_its_kinds()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);

        var created = await (await Create(author, novel, EveryKind)).OkJson();
        var chapterId = created.GetProperty("id").GetGuid();

        // The created chapter, the author's chapter (what an editor loads) and the reader's chapter say the same.
        var authors = await (await api.Get($"/api/myworks/{novel.Id}/chapters/{chapterId}", author)).OkJson();
        var readers = await (await api.Get($"/api/novel/{novel.Id}/chapter/{chapterId}?prefetch=true")).OkJson();
        foreach (var chapter in new[] { created, authors, readers })
        {
            Assert.Equal(EveryKindStored, Paragraphs(chapter).Select(p => (p.Kind, p.Content, p.Caption)));
            Assert.Equal(Enumerable.Range(0, EveryKindStored.Length), Paragraphs(chapter).Select(p => p.OrderIndex));
        }

        await using var db = api.Db();
        var stored = await db.ChapterParagraphs.AsNoTracking().Where(p => p.ChapterId == chapterId).OrderBy(p => p.OrderIndex).ToListAsync();
        Assert.Equal(EveryKindStored, stored.Select(p => (p.ContentType, p.Content, p.Caption)));
        Assert.All(stored, p => Assert.Equal(ParagraphText.Hash(p.Content), p.ContentHash));
        var row = await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == chapterId);
        Assert.Equal(EveryKindStored.Length, row.ParagraphsCount);
        Assert.Null(row.Content);
    }

    [Fact]
    public async Task Saving_it_back_as_loaded_changes_nothing()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var chapterId = (await (await Create(author, novel, EveryKind)).OkJson()).GetProperty("id").GetGuid();
        var loaded = Paragraphs(await (await api.Get($"/api/myworks/{novel.Id}/chapters/{chapterId}", author)).OkJson());
        var before = await Stored(chapterId);

        await Save(author, novel, chapterId, ChapterFormatTestsWire(loaded));

        Assert.Equal(before, await Stored(chapterId));
    }

    [Fact]
    public async Task The_web_editors_output_round_trips()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        const string webEditor =
            "<p class=\"min-h-[1em]\">في الصباح وصل فادي.<br>كان الباب مغلقاً.</p><p class=\"min-h-[1em]\"></p>" +
            "<p class=\"min-h-[1em]\"><strong>«حسناً»</strong> قال، ثم <em>صمت</em> &amp; <u>ابتسم</u> <s>قليلاً</s>.</p>" +
            "<p class=\"min-h-[1em]\">مكتوب: &lt;&lt;اليوم&gt;&gt;&nbsp;</p>";

        var chapterId = (await (await Create(author, novel, webEditor)).OkJson()).GetProperty("id").GetGuid();
        var loaded = Paragraphs(await (await api.Get($"/api/myworks/{novel.Id}/chapters/{chapterId}", author)).OkJson());
        Assert.Equal(
            ["في الصباح وصل فادي.<br>كان الباب مغلقاً.", "<strong>«حسناً»</strong> قال، ثم <em>صمت</em> &amp; <u>ابتسم</u> <s>قليلاً</s>.",
             "مكتوب: &lt;&lt;اليوم&gt;&gt;&nbsp;"],
            loaded.Select(p => p.Content));
        Assert.All(loaded, p => Assert.Equal(ParagraphKinds.Text, p.Kind));

        // The web editor loads each paragraph as a <p> of its own (sard-frontend paragraphsToEditorHtml) and saves it.
        var before = await Stored(chapterId);
        await Save(author, novel, chapterId, string.Concat(loaded.Select(p => $"<p class=\"min-h-[1em]\">{p.Content}</p>")));
        Assert.Equal(before, await Stored(chapterId));
    }

    [Fact]
    public async Task Changing_only_a_paragraphs_kind_or_formatting_keeps_it_and_its_comments()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);
        var chapterId = (await (await Create(author, novel, "<p>بيت من الشعر</p><p>رسالة قديمة</p><p>فقرة أخيرة</p>")).OkJson())
            .GetProperty("id").GetGuid();
        var paragraphs = await Stored(chapterId);
        foreach (var paragraph in paragraphs.Take(2))
        {
            await (await api.Send(HttpMethod.Post, $"/api/comment/paragraph/{paragraph.Id}", reader,
                ReaderApi.Form(("Content", "تعليق قارئ")))).OkJson();
        }

        // The first becomes a centered paragraph, the second a quote in bold; the words stay.
        await Save(author, novel, chapterId,
            "<p data-kind=\"center\">بيت من الشعر</p><p data-kind=\"quote\"><strong>رسالة</strong> قديمة</p><p>فقرة أخيرة</p>");

        var saved = await Stored(chapterId);
        Assert.Equal(paragraphs.Select(p => p.Id), saved.Select(p => p.Id));
        Assert.Equal(
            [(ParagraphKinds.Center, "بيت من الشعر"), (ParagraphKinds.Quote, "<strong>رسالة</strong> قديمة"), (ParagraphKinds.Text, "فقرة أخيرة")],
            saved.Select(p => (p.Kind, p.Content)));
        Assert.Equal([1, 1, 0], saved.Select(p => p.CommentsCount));
        foreach (var paragraph in saved.Take(2))
        {
            var comments = await (await api.Get($"/api/comment/paragraph/{paragraph.Id}")).OkJson();
            Assert.Equal(1, comments.GetProperty("items").GetArrayLength());
        }

        var read = Paragraphs(await (await api.Get($"/api/novel/{novel.Id}/chapter/{chapterId}?prefetch=true")).OkJson());
        Assert.Equal(saved.Select(p => (p.Id, p.Kind, p.Content)), read.Select(p => (p.Id, p.Kind, p.Content)));

        // A changed word is still a new paragraph, and its comments go with the old one.
        await Save(author, novel, chapterId,
            "<p data-kind=\"center\">بيت من الشعر الجديد</p><p data-kind=\"quote\"><strong>رسالة</strong> قديمة</p><p>فقرة أخيرة</p>");
        var edited = await Stored(chapterId);
        Assert.NotEqual(saved[0].Id, edited[0].Id);
        Assert.Equal(saved.Skip(1).Select(p => p.Id), edited.Skip(1).Select(p => p.Id));
        Assert.Equal([0, 1, 0], edited.Select(p => p.CommentsCount));
    }

    [Fact]
    public async Task The_length_limit_counts_the_text_readers_see()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);

        // 90,000 visible characters in 3,000 bold paragraphs: some 150,000 characters as sent, past the old limit.
        var formatted = string.Concat(Enumerable.Range(0, 3_000).Select(_ => "<p class=\"min-h-[1em]\"><strong>" + new string('ن', 30) + "</strong></p>"));
        Assert.True(formatted.Length > ChapterTextRules.VisibleTextMaxLength);
        var created = await Create(author, novel, formatted);
        var chapterId = (await created.OkJson()).GetProperty("id").GetGuid();
        (await Send(author, novel, chapterId, new { title = "فصل", content = formatted + "<p>المزيد</p>" })).EnsureSuccessStatusCode();

        // Over 100,000 visible characters, however little markup: refused with the message it always had.
        var tooLong = "<p>" + new string('ن', ChapterTextRules.VisibleTextMaxLength + 1) + "</p>";
        await AssertRefused(await Create(author, novel, tooLong), "يجب ألا يتجاوز نص الفصل 100000 حرف");
        await AssertRefused(await Send(author, novel, chapterId, new { title = "فصل", content = tooLong }), "يجب ألا يتجاوز نص الفصل 100000 حرف");

        // Exactly 100,000 is allowed; the spaces and markup around it count nothing.
        var atLimit = "<p>  <em>" + new string('ن', ChapterTextRules.VisibleTextMaxLength) + "</em>  </p>";
        (await Send(author, novel, chapterId, new { title = "فصل", content = atLimit })).EnsureSuccessStatusCode();

        // As sent, formatting included, the text may not pass 400,000 characters.
        var tooLarge = string.Concat(Enumerable.Range(0, 20_001).Select(_ => "<p><strong>ن</strong></p>"));
        Assert.True(tooLarge.Length > ChapterTextRules.ContentMaxLength);
        await AssertRefused(await Create(author, novel, tooLarge), "يجب ألا يتجاوز نص الفصل مع تنسيقه 400000 حرف");
        await AssertRefused(await Send(author, novel, chapterId, new { title = "فصل", content = tooLarge }),
            "يجب ألا يتجاوز نص الفصل مع تنسيقه 400000 حرف");
    }

    [Fact]
    public async Task Paragraphs_stored_before_the_format_leave_the_api_clean()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);
        var (chapter, paragraphs) = await api.AddChapter(novel,
            "<p class=\"min-h-[1em]\">كان <strong>الليل</strong> طويلاً",
            "<p onmouseover=\"alert(1)\">أ<script>alert(1)</script><img src=x onerror=\"alert(1)\">ب<iframe src=\"https://x.test\"></iframe>",
            "<p class=\"min-h-[1em]\"><img src=\"https://files.test/a.png\" onerror=\"alert(1)\">",
            "<a href=\"javascript:alert(1)\" style=\"x\">رابط</a> <span onclick=\"x\">نص</span>");
        string[] expected = ["كان <strong>الليل</strong> طويلاً", "أب", "https://files.test/a.png", "رابط نص"];

        var read = Paragraphs(await (await api.Get($"/api/novel/{novel.Id}/chapter/{chapter.Id}?prefetch=true", reader)).OkJson());
        var authors = Paragraphs(await (await api.Get($"/api/myworks/{novel.Id}/chapters/{chapter.Id}", author)).OkJson());
        foreach (var served in new[] { read, authors })
        {
            Assert.Equal(paragraphs.Select(p => p.Id), served.Select(p => p.Id));
            Assert.Equal(expected, served.Select(p => p.Content));
            Assert.Equal([ParagraphKinds.Text, ParagraphKinds.Text, ParagraphKinds.Image, ParagraphKinds.Text], served.Select(p => p.Kind));
        }

        // The quote of a paragraph next to a comment on it: the comment context and the commenter's comment list.
        var posted = await api.Send(HttpMethod.Post, $"/api/comment/paragraph/{paragraphs[1].Id}", reader,
            ReaderApi.Form(("Content", "تعليق")));
        var commentId = (await posted.OkJson()).GetProperty("comment").GetProperty("id").GetGuid();
        var context = (await (await api.Get($"/api/notifications/comment/{commentId}", reader)).OkJson()).GetProperty("context");
        Assert.Equal("أب", context.GetProperty("paragraphExcerpt").GetString());
        var listed = await (await api.Get($"/api/User/{reader.UserName}/comments", reader)).OkJson();
        Assert.Equal("أب", listed.GetProperty("items").EnumerateArray().Single().GetProperty("paragraphExcerpt").GetString());
    }

    // ---- Helpers ----

    private sealed record Paragraph(Guid Id, string Kind, string Content, string? Caption, int OrderIndex, int CommentsCount);

    private static List<Paragraph> Paragraphs(JsonElement chapter) => chapter.GetProperty("paragraphs").EnumerateArray()
        .Select(p => new Paragraph(
            p.GetProperty("id").GetGuid(),
            p.GetProperty("contentType").GetString()!,
            p.GetProperty("content").GetString()!,
            p.GetProperty("caption").GetString(),
            p.GetProperty("orderIndex").GetInt32(),
            p.GetProperty("commentsCount").GetInt32()))
        .ToList();

    /// <summary>The chapter's stored paragraphs, in order, with what can change in them.</summary>
    private async Task<List<Paragraph>> Stored(Guid chapterId)
    {
        await using var db = api.Db();
        return (await db.ChapterParagraphs.AsNoTracking().Where(p => p.ChapterId == chapterId).OrderBy(p => p.OrderIndex).ToListAsync())
            .Select(p => new Paragraph(p.Id, p.ContentType, p.Content, p.Caption, p.OrderIndex, p.CommentsCount)).ToList();
    }

    /// <summary>Paragraphs as an editor that loaded them would send them back.</summary>
    private static string ChapterFormatTestsWire(IEnumerable<Paragraph> paragraphs) =>
        Unit.ChapterFormatTests.Wire(paragraphs.Select(p => new FormattedParagraph(p.Kind, p.Content, p.Caption)));

    private Task<HttpResponseMessage> Create(ApiUser author, Novel novel, string content) =>
        api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status = ChapterStatuses.Published, title = "فصل " + Seed.Marker(), content }));

    private Task<HttpResponseMessage> Send(ApiUser author, Novel novel, Guid chapterId, object body) =>
        api.Send(HttpMethod.Patch, $"/api/novel/{novel.Id}/chapter/{chapterId}", author, JsonContent.Create(body));

    private async Task Save(ApiUser author, Novel novel, Guid chapterId, string content) =>
        await (await Send(author, novel, chapterId, new { title = "فصل", content })).OkJson();

    private static async Task AssertRefused(HttpResponseMessage response, string message)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{(int)response.StatusCode}: {body}");
        var error = JsonDocument.Parse(body).RootElement;
        Assert.Equal("ValidationFailed", error.GetProperty("code").GetString());
        Assert.Equal(message, error.GetProperty("message").GetString());
    }
}
