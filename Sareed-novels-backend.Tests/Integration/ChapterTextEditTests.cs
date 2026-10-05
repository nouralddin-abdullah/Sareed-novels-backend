using Application.Chapters.Paragraphs;
using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// An edit of a chapter's text (IChapterParagraphsRepository.BeginEditAsync), which the author's saves and the format
/// maintenance (#74) go through: two edits of the same chapter run one after another, the second reading what the first
/// saved; edits of other chapters don't wait; an edit given up changes nothing.
/// </summary>
public class ChapterTextEditTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    [Fact]
    public async Task Edits_of_one_chapters_text_run_one_after_another_and_others_do_not_wait()
    {
        var (chapter, other) = (await SeedChapter("أول"), await SeedChapter("آخر"));

        await using var firstDb = database.CreateContext();
        var first = await new ChapterParagraphsRepository(firstDb).BeginEditAsync(chapter);

        await using var secondDb = database.CreateContext();
        var second = new ChapterParagraphsRepository(secondDb).BeginEditAsync(chapter);

        // Another chapter's edit doesn't wait for it.
        await using (var otherDb = database.CreateContext())
        {
            var started = new ChapterParagraphsRepository(otherDb).BeginEditAsync(other);
            Assert.Same(started, await Task.WhenAny(started, Task.Delay(TimeSpan.FromSeconds(10))));
            await (await started).DisposeAsync();
        }

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(second.IsCompleted);

        // The first saves and ends: the second goes on, and reads what the first saved.
        var paragraph = first.Paragraphs.Single();
        ParagraphRows.Store(paragraph, new FormattedParagraph("center", "أول", null));
        var added = ParagraphRows.New(chapter, new FormattedParagraph("text", "جديد", null), 1, DateTime.UtcNow);
        await first.SaveAsync([paragraph, added], []);
        await first.DisposeAsync();

        Assert.Same(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(30))));
        await using var edit = await second;
        Assert.Equal([("center", "أول"), ("text", "جديد")], edit.Paragraphs.Select(p => (p.ContentType, p.Content)));

        await using var db = database.CreateContext();
        Assert.Equal(2, (await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == chapter)).ParagraphsCount);
    }

    [Fact]
    public async Task An_edit_given_up_changes_nothing()
    {
        var chapter = await SeedChapter("نص");

        await using var db = database.CreateContext();
        var repository = new ChapterParagraphsRepository(db);
        await using (var edit = await repository.BeginEditAsync(chapter))
        {
            var paragraph = edit.Paragraphs.Single();
            paragraph.Content = "تغيير لم يُحفظ";
            paragraph.OrderIndex = 5;
        }

        // Nothing was written, and the context that held the edit doesn't keep the change for a later save.
        await db.SaveChangesAsync();
        await using var check = database.CreateContext();
        var stored = await check.ChapterParagraphs.AsNoTracking().SingleAsync(p => p.ChapterId == chapter);
        Assert.Equal(("نص", 0), (stored.Content, stored.OrderIndex));

        // The chapter's text is free again.
        await using var next = await repository.BeginEditAsync(chapter);
        Assert.Equal("نص", next.Paragraphs.Single().Content);
    }

    private async Task<Guid> SeedChapter(string text)
    {
        var author = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        var chapter = Seed.Chapters(novel, 1, DateTime.UtcNow).Single();
        chapter.ParagraphsCount = 1;

        await using var db = database.CreateContext();
        db.Users.Add(author);
        db.Novels.Add(novel);
        db.Chapters.Add(chapter);
        db.ChapterParagraphs.Add(ParagraphRows.New(chapter.Id, new FormattedParagraph("text", text, null), 0, DateTime.UtcNow));
        await db.SaveChangesAsync();
        return chapter.Id;
    }
}
