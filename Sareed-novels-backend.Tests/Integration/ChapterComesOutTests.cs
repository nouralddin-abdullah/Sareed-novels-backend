using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #39: a chapter comes out when it is first published, created published or a draft published later. Only then is it
/// news: the novel's LastUpdatedAt moves to that time (the "last updated" sort, the novel page, my works and the sitemap)
/// and the novel's readers are told, a notification and a push each. Writing a draft, saving or editing a chapter that
/// is out, unpublishing it and publishing it again change neither. Through the real handlers, on a clock.
/// </summary>
public class ChapterComesOutTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly DateTime Start = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly ChapterDesk desk = new(database, Start);

    [Fact]
    public async Task A_chapter_created_published_comes_out_when_it_is_created_and_a_draft_does_not()
    {
        var (author, novel) = await desk.SeedNovel();

        var published = await desk.Create(author, novel, ChapterStatuses.Published);
        Assert.Equal(Start, await LastUpdated(novel));
        Assert.Equal(1, desk.TimesAnnounced(published));

        desk.Clock.Advance(TimeSpan.FromDays(1));
        var draft = await desk.Create(author, novel, ChapterStatuses.Draft);
        Assert.Equal(Start, await LastUpdated(novel));
        Assert.Equal(0, desk.TimesAnnounced(draft));
    }

    [Fact]
    public async Task A_draft_written_before_the_last_update_and_published_after_it_counts_from_its_publish_date()
    {
        var (author, novel) = await desk.SeedNovel();
        var draft = await desk.Create(author, novel, ChapterStatuses.Draft);
        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Create(author, novel, ChapterStatuses.Published);
        var lastUpdate = Start.AddDays(1);

        // Saved again as a draft (the editor sends the status with every save): still nothing.
        desk.Clock.Advance(TimeSpan.FromDays(2));
        await desk.Save(author, novel, draft, ChapterStatuses.Draft);
        Assert.Equal(lastUpdate, await LastUpdated(novel));
        Assert.Equal(0, desk.TimesAnnounced(draft));

        // Written before the last update, published after it: it counts from when it was published.
        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, draft, ChapterStatuses.Published);
        Assert.Equal(Start.AddDays(4), await LastUpdated(novel));
        Assert.Equal(1, desk.TimesAnnounced(draft));
    }

    [Fact]
    public async Task Unpublishing_and_publishing_again_move_nothing_and_tell_no_one_again()
    {
        var (author, novel) = await desk.SeedNovel();
        var publishedLater = await desk.Create(author, novel, ChapterStatuses.Draft);
        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, publishedLater, ChapterStatuses.Published);
        var createdPublished = await desk.Create(author, novel, ChapterStatuses.Published);
        var cameOut = Start.AddDays(1);

        foreach (var chapter in new[] { publishedLater, createdPublished })
        {
            desk.Clock.Advance(TimeSpan.FromDays(1));
            await desk.Save(author, novel, chapter, ChapterStatuses.Draft);
            Assert.Equal(cameOut, await LastUpdated(novel));

            desk.Clock.Advance(TimeSpan.FromDays(1));
            await desk.Save(author, novel, chapter, ChapterStatuses.Published);
            // Saved again as the editor does, with the status or without it.
            await desk.Save(author, novel, chapter, ChapterStatuses.Published);
            await desk.Save(author, novel, chapter, status: null);

            Assert.Equal(cameOut, await LastUpdated(novel));
            Assert.Equal(1, desk.TimesAnnounced(chapter));
        }
    }

    [Fact]
    public async Task Editing_a_published_chapter_moves_nothing_and_tells_no_one()
    {
        // As before #39: a correction or a rewrite of a chapter that is out isn't a new chapter.
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Published);

        desk.Clock.Advance(TimeSpan.FromDays(3));
        await desk.Save(author, novel, chapter, ChapterStatuses.Published, "<p>نص معدل</p><p>وفقرة جديدة</p>");

        Assert.Equal(Start, await LastUpdated(novel));
        Assert.Equal(1, desk.TimesAnnounced(chapter));
    }

    [Fact]
    public async Task The_sitemap_moves_when_a_chapter_comes_out_only()
    {
        var (author, novel) = await desk.SeedNovel();
        var first = await desk.Create(author, novel, ChapterStatuses.Published);
        desk.Clock.Advance(TimeSpan.FromDays(1));
        var draft = await desk.Create(author, novel, ChapterStatuses.Draft);
        await AssertSitemap(novel, Start, (first, Start));

        desk.Clock.Advance(TimeSpan.FromDays(2));
        await desk.Save(author, novel, draft, ChapterStatuses.Published);
        var cameOut = Start.AddDays(3);
        await AssertSitemap(novel, cameOut, (first, Start), (draft, cameOut));

        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, draft, ChapterStatuses.Draft);
        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, draft, ChapterStatuses.Published);
        await AssertSitemap(novel, cameOut, (first, Start), (draft, cameOut));
    }

    [Fact]
    public async Task Of_two_saves_that_publish_a_draft_at_once_only_one_brings_it_out_and_an_older_copy_keeps_its_date()
    {
        var (author, novel) = await desk.SeedNovel();
        var draftId = await desk.Create(author, novel, ChapterStatuses.Draft);

        // Three requests load the draft before any of them saves: two publish it, one saves it as a draft.
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        await using var stale = database.CreateContext();
        var (firstCopy, secondCopy, staleCopy) = (await Load(first), await Load(second), await Load(stale));

        firstCopy.SetStatus(ChapterStatuses.Published, Start.AddMinutes(1));
        var firstSave = await new ChaptersRepository(first).UpdateChapter(firstCopy);
        secondCopy.SetStatus(ChapterStatuses.Published, Start.AddMinutes(2));
        var secondSave = await new ChaptersRepository(second).UpdateChapter(secondCopy);
        staleCopy.SetStatus(ChapterStatuses.Draft, Start.AddMinutes(3));
        var staleSave = await new ChaptersRepository(stale).UpdateChapter(staleCopy);

        Assert.Equal(new ChapterSave(Saved: true, CameOut: true), firstSave);
        Assert.Equal(new ChapterSave(Saved: true, CameOut: false), secondSave);
        Assert.Equal(new ChapterSave(Saved: true, CameOut: false), staleSave);
        // The draft save came last, so it is a draft again, but it came out at the first save and keeps that date.
        await using var check = database.CreateContext();
        var stored = await check.Chapters.SingleAsync(c => c.Id == draftId);
        Assert.Equal((ChapterStatuses.Draft, (DateTime?)Start.AddMinutes(1)), (stored.Status, stored.PublishedAt));
        Assert.Equal(Start.AddMinutes(1), firstCopy.PublishedAt);

        async Task<Chapter> Load(Infrastructure.Persistence.ApplicationDbContext db) =>
            (await new ChaptersRepository(db).GetChapterById(draftId))!;
    }

    private async Task<DateTime> LastUpdated(Novel novel)
    {
        await using var db = database.CreateContext();
        return await db.Novels.Where(n => n.Id == novel.Id).Select(n => n.LastUpdatedAt).SingleAsync();
    }

    /// <summary>The novel's lastmod in the sitemap data, and its published chapters' in order.</summary>
    private async Task AssertSitemap(Novel novel, DateTime lastModified, params (Guid Id, DateTime At)[] chapters)
    {
        await using var db = database.CreateContext();
        var entry = Assert.Single(await new NovelsRepository(db).GetSitemapEntriesAsync(), e => e.Id == novel.Id);
        Assert.Equal(lastModified, entry.LastModified);
        Assert.Equal(chapters.Select(c => (c.Id, (DateTime?)c.At)), entry.Chapters.Select(c => (c.Id, c.LastModified)));
    }
}
