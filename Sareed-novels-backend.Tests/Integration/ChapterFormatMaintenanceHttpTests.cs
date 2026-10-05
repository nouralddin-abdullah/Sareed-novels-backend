using System.Net;
using System.Text.Json;
using Application.Chapters.Paragraphs;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The chapter format maintenance (#74, POST /api/admin/chapters/clean-format), on paragraphs stored the ways
/// production and older clients stored them: admins only; a dry run by default that changes nothing and reports what the
/// real pass does; a real pass that stores format v1, splits a picture out of its text (the paragraph keeping its id and
/// comments), removes empty paragraphs nobody commented on, keeps ContentHash right, and leaves alone (and reports) any
/// paragraph whose words or picture the cleaning would lose; and that changes nothing when run again. Its own database:
/// the pass goes over every chapter.
/// </summary>
public class ChapterFormatMaintenanceHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private const string Url = "/api/admin/chapters/clean-format";
    private const string P = "<p class=\"min-h-[1em]\">";

    [Fact]
    public async Task Only_admins_can_run_it()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Send(HttpMethod.Post, Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Send(HttpMethod.Post, Url, await api.SignUp())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Send(HttpMethod.Post, Url + "?dryRun=false", await api.SignUp())).StatusCode);
    }

    [Fact]
    public async Task A_dry_run_changes_nothing_the_real_pass_converts_and_a_second_pass_changes_nothing()
    {
        var admin = await api.SignUpAdmin();
        var world = await SeedChapters();
        var before = await Snapshot();

        // The dry run, the default: the report, and not one change.
        var dry = await (await api.Send(HttpMethod.Post, Url, admin)).OkJson();
        Assert.True(dry.GetProperty("dryRun").GetBoolean());
        AssertCounts(dry);
        Assert.Equal(before, await Snapshot());

        var skipped = dry.GetProperty("skipped").EnumerateArray().ToDictionary(s => s.GetProperty("paragraphId").GetGuid(), s => s);
        Assert.Equal("EmptyWithComments", skipped[world.EmptyWithComment].GetProperty("reason").GetString());
        Assert.Equal("PictureDropped", skipped[world.DataPicture].GetProperty("reason").GetString());
        var changedWords = skipped[world.Script];
        Assert.Equal("VisibleTextChanged", changedWords.GetProperty("reason").GetString());
        Assert.Equal("أalert(1)ب", changedWords.GetProperty("wordsBefore").GetString());
        Assert.Equal("أب", changedWords.GetProperty("wordsAfter").GetString());

        var split = dry.GetProperty("examples").EnumerateArray().Single(e => e.GetProperty("change").GetString() == "split");
        Assert.Equal(world.Picture, split.GetProperty("paragraphId").GetGuid());
        Assert.Equal(P + "قبل <img src=\"https://files.test/a.png\"> بعد", split.GetProperty("before").GetProperty("content").GetString());
        Assert.Equal([(ParagraphKinds.Text, "قبل"), (ParagraphKinds.Image, "https://files.test/a.png"), (ParagraphKinds.Text, "بعد")],
            split.GetProperty("after").EnumerateArray().Select(a => (a.GetProperty("kind").GetString(), a.GetProperty("content").GetString())));
        // Chapters go in id order; each change has up to three examples, here every rewritten paragraph.
        var rewritten = dry.GetProperty("examples").EnumerateArray()
            .Single(e => e.GetProperty("change").GetString() == "rewritten" && e.GetProperty("paragraphId").GetGuid() == world.Legacy);
        Assert.Equal(P + "كان <strong>الليل</strong> طويلاً", rewritten.GetProperty("before").GetProperty("content").GetString());
        Assert.Equal("كان <strong>الليل</strong> طويلاً", rewritten.GetProperty("after")[0].GetProperty("content").GetString());

        // The real pass.
        var real = await (await api.Send(HttpMethod.Post, Url + "?dryRun=false", admin)).OkJson();
        Assert.False(real.GetProperty("dryRun").GetBoolean());
        AssertCounts(real);
        Assert.Empty(real.GetProperty("failures").EnumerateArray());

        await using (var db = api.Db())
        {
            var a = await Rows(db, world.ChapterA);
            Assert.Equal(
            [
                (world.Legacy, ParagraphKinds.Text, "كان <strong>الليل</strong> طويلاً"),
                (world.Picture, ParagraphKinds.Text, "قبل"),
                (a[2].Id, ParagraphKinds.Image, "https://files.test/a.png"),
                (a[3].Id, ParagraphKinds.Text, "بعد"),
                (world.EmptyWithComment, ParagraphKinds.Text, P + "<br>"),
                (world.Script, ParagraphKinds.Text, "<p>أ<script>alert(1)</script>ب"),
                (world.DataPicture, ParagraphKinds.Text, "<p>صورة <img src=\"data:image/png;base64,AAAA\"> هنا"),
                (world.WrongHash, ParagraphKinds.Text, "نص صحيح"),
                (world.Clean, ParagraphKinds.Text, "نص سليم")
            ], a.Select(p => (p.Id, p.ContentType, p.Content)));
            Assert.Equal(Enumerable.Range(0, 9), a.Select(p => p.OrderIndex));
            Assert.All(a, p => Assert.Equal(ParagraphText.Hash(p.Content), p.ContentHash));
            Assert.DoesNotContain(world.Empty, a.Select(p => p.Id));
            Assert.Equal(9, (await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == world.ChapterA)).ParagraphsCount);

            // The split paragraph keeps its comment; the empty one with a comment is still there with its own.
            Assert.Equal([1, 1], a.Where(p => p.Id == world.Picture || p.Id == world.EmptyWithComment).Select(p => p.CommentsCount));
            Assert.Equal(2, await db.Comments.CountAsync(c => c.ParagraphId == world.Picture || c.ParagraphId == world.EmptyWithComment));
            Assert.Equal(2, (await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == world.ChapterA)).TotalCommentsCount);

            var b = await Rows(db, world.ChapterB);
            Assert.Equal([(ParagraphKinds.Image, "https://files.test/B.png"), (ParagraphKinds.Break, "* * *")],
                b.Select(p => (p.ContentType, p.Content)));
            Assert.All(b, p => Assert.Equal(ParagraphText.Hash(p.Content), p.ContentHash));

            // Nothing the author changed: the paragraphs' UpdatedAt is left.
            Assert.All(a.Concat(b), p => Assert.Null(p.UpdatedAt));
        }

        // What readers get now is what was stored.
        var read = await (await api.Get($"/api/novel/{world.NovelId}/chapter/{world.ChapterA}?prefetch=true")).OkJson();
        Assert.Equal(["كان <strong>الليل</strong> طويلاً", "قبل", "https://files.test/a.png", "بعد"],
            read.GetProperty("paragraphs").EnumerateArray().Take(4).Select(p => p.GetProperty("content").GetString()));

        // Again: everything is in format v1 or left alone, so nothing changes.
        var afterReal = await Snapshot();
        var again = await (await api.Send(HttpMethod.Post, Url + "?dryRun=false", admin)).OkJson();
        Assert.Equal(0, again.GetProperty("paragraphsChanged").GetInt32());
        Assert.Equal(0, again.GetProperty("chaptersChanged").GetInt32());
        Assert.Equal(3, again.GetProperty("paragraphsSkipped").GetInt32());
        Assert.Equal(afterReal, await Snapshot());
    }

    // ---- The seeded chapters ----

    private sealed record World(
        Guid NovelId, Guid ChapterA, Guid ChapterB, Guid Legacy, Guid Picture, Guid Empty, Guid EmptyWithComment,
        Guid Script, Guid DataPicture, Guid WrongHash, Guid Clean);

    /// <summary>
    /// Chapter A, as production and older clients stored paragraphs; chapter B, a stored picture and break; chapter C,
    /// already format v1; chapter D, text only in the legacy Chapters.Content column.
    /// </summary>
    private async Task<World> SeedChapters()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);

        await using var db = api.Db();
        var chapters = Seed.Chapters(novel, 4, DateTime.UtcNow.AddDays(-100));
        chapters[0].Content = "<p>نسخة قديمة من النص</p>";
        chapters[1].Content = null;
        chapters[2].Content = null;
        chapters[3].Content = "<p>نص فصل قديم بلا فقرات</p>";
        db.Chapters.AddRange(chapters);

        ChapterParagraph Row(Chapter chapter, int index, string content, string kind = ParagraphKinds.Text, string? hash = null) => new()
        {
            Id = Guid.NewGuid(), ChapterId = chapter.Id, Content = content, ContentType = kind,
            ContentHash = hash ?? ParagraphText.Hash(content), OrderIndex = index, CreatedAt = DateTime.UtcNow.AddDays(-100)
        };

        var a = new[]
        {
            Row(chapters[0], 0, P + "كان <strong>الليل</strong> طويلاً"),
            Row(chapters[0], 1, P + "قبل <img src=\"https://files.test/a.png\"> بعد"),
            Row(chapters[0], 2, P),
            Row(chapters[0], 3, P + "<br>"),
            Row(chapters[0], 4, "<p>أ<script>alert(1)</script>ب"),
            Row(chapters[0], 5, "<p>صورة <img src=\"data:image/png;base64,AAAA\"> هنا"),
            Row(chapters[0], 6, "نص صحيح", hash: "not-its-hash"),
            Row(chapters[0], 7, "نص سليم")
        };
        var b = new[]
        {
            Row(chapters[1], 0, "HTTPS://Files.Test/B.png", ParagraphKinds.Image),
            Row(chapters[1], 1, "---", ParagraphKinds.Break)
        };
        var c = Row(chapters[2], 0, "فقرة جاهزة");
        db.ChapterParagraphs.AddRange(a.Concat(b).Append(c));
        chapters[0].ParagraphsCount = a.Length;
        chapters[1].ParagraphsCount = b.Length;
        chapters[2].ParagraphsCount = 1;
        await db.SaveChangesAsync();

        // A comment on the paragraph with a picture, and one on an empty paragraph.
        var comments = new CommentsRepository(db);
        foreach (var paragraph in new[] { a[1], a[3] })
        {
            await comments.CreateComment(new Comments { Id = Guid.NewGuid(), UserId = reader.Id, ParagraphId = paragraph.Id, Content = "تعليق" });
        }

        return new World(novel.Id, chapters[0].Id, chapters[1].Id, a[0].Id, a[1].Id, a[2].Id, a[3].Id, a[4].Id, a[5].Id,
            a[6].Id, a[7].Id);
    }

    /// <summary>The counts the seeded chapters give, in the dry run and the real pass alike.</summary>
    private static void AssertCounts(JsonElement report)
    {
        Assert.Equal(4, report.GetProperty("chaptersChecked").GetInt32());
        Assert.Equal(2, report.GetProperty("chaptersChanged").GetInt32());
        Assert.Equal(11, report.GetProperty("paragraphsChecked").GetInt32());
        Assert.Equal(2, report.GetProperty("paragraphsUnchanged").GetInt32());
        Assert.Equal(6, report.GetProperty("paragraphsChanged").GetInt32());
        Assert.Equal(3, report.GetProperty("paragraphsRewritten").GetInt32());
        Assert.Equal(1, report.GetProperty("paragraphsSplit").GetInt32());
        Assert.Equal(2, report.GetProperty("paragraphsAdded").GetInt32());
        Assert.Equal(1, report.GetProperty("emptyParagraphsRemoved").GetInt32());
        Assert.Equal(1, report.GetProperty("hashesFixed").GetInt32());
        Assert.Equal(3, report.GetProperty("paragraphsSkipped").GetInt32());
        Assert.Equal(3, report.GetProperty("skipped").GetArrayLength());

        var pictures = report.GetProperty("pictures");
        Assert.Equal((3, 2, 1), (pictures.GetProperty("paragraphs").GetInt32(), pictures.GetProperty("kept").GetInt32(),
            pictures.GetProperty("notKept").GetInt32()));
        Assert.Equal(3, pictures.GetProperty("examples").GetArrayLength());

        var legacy = report.GetProperty("legacyChapterContent");
        Assert.Equal((2, 1), (legacy.GetProperty("chapters").GetInt32(), legacy.GetProperty("withoutParagraphs").GetInt32()));
        Assert.Equal(JsonValueKind.Null, report.GetProperty("nextCursor").ValueKind);
    }

    private static Task<List<ChapterParagraph>> Rows(Infrastructure.Persistence.ApplicationDbContext db, Guid chapterId) =>
        db.ChapterParagraphs.AsNoTracking().Where(p => p.ChapterId == chapterId).OrderBy(p => p.OrderIndex).ToListAsync();

    /// <summary>Every paragraph and chapter column the pass could change.</summary>
    private async Task<List<string>> Snapshot()
    {
        await using var db = api.Db();
        var paragraphs = await db.ChapterParagraphs.AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => $"{p.Id}|{p.ChapterId}|{p.OrderIndex}|{p.ContentType}|{p.Content}|{p.ContentHash}|{p.Caption}|{p.UpdatedAt}|{p.CommentsCount}")
            .ToListAsync();
        var chapters = await db.Chapters.AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => $"{c.Id}|{c.ParagraphsCount}|{c.TotalCommentsCount}|{c.Content}")
            .ToListAsync();
        return paragraphs.Concat(chapters).ToList();
    }
}

/// <summary>The format maintenance goes through every chapter in batches, and from a cursor. Its own database.</summary>
public class ChapterFormatMaintenanceBatchHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private const string Url = "/api/admin/chapters/clean-format";

    [Fact]
    public async Task It_goes_through_the_chapters_in_batches_and_from_a_cursor()
    {
        var admin = await api.SignUpAdmin();
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        for (var i = 0; i < 3; i++)
        {
            await api.AddChapter(novel, "<p class=\"min-h-[1em]\">فقرة " + i);
        }

        List<Guid> ids;
        await using (var db = api.Db())
        {
            // In the server's order of ids, which the cursor follows.
            ids = await db.Chapters.OrderBy(c => c.Id).Select(c => c.Id).ToListAsync();
        }

        var all = await (await api.Send(HttpMethod.Post, Url + "?batchSize=1", admin)).OkJson();
        Assert.Equal(ids.Count, all.GetProperty("chaptersChecked").GetInt32());
        Assert.Equal(JsonValueKind.Null, all.GetProperty("nextCursor").ValueKind);

        var rest = await (await api.Send(HttpMethod.Post, $"{Url}?batchSize=2&after={ids[^3]}", admin)).OkJson();
        Assert.Equal(2, rest.GetProperty("chaptersChecked").GetInt32());
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("nextCursor").ValueKind);

        var none = await (await api.Send(HttpMethod.Post, $"{Url}?after={ids[^1]}", admin)).OkJson();
        Assert.Equal(0, none.GetProperty("chaptersChecked").GetInt32());
    }
}
