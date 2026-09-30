using Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #33: every way a chapter becomes published stamps when it came out, on the handlers' clock: created published
/// (CreateChapterCommandHandler) and a draft published later (UpdateChapterCommandHandler). The first publish is
/// kept through unpublishing, publishing again and later saves; drafts that were never published have none.
/// </summary>
public class ChapterPublishingTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly DateTime Written = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly ChapterDesk desk = new(database, Written);

    private sealed record Stored(string Status, DateTime CreatedAt, DateTime? PublishedAt);

    [Fact]
    public async Task A_chapter_created_published_comes_out_when_it_is_created_and_a_draft_does_not()
    {
        var (author, novel) = await desk.SeedNovel();

        var published = await desk.Create(author, novel, ChapterStatuses.Published);
        desk.Clock.Advance(TimeSpan.FromMinutes(5));
        var draft = await desk.Create(author, novel, ChapterStatuses.Draft);

        Assert.Equal(new Stored(ChapterStatuses.Published, Written, Written), await Chapter(published));
        Assert.Equal(new Stored(ChapterStatuses.Draft, Written.AddMinutes(5), null), await Chapter(draft));
    }

    [Fact]
    public async Task A_draft_published_later_comes_out_when_it_is_published_not_when_it_was_written()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft);

        // Saved as a draft again first (the editor sends the status with every save): still not out.
        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, chapter, ChapterStatuses.Draft);
        Assert.Equal(new Stored(ChapterStatuses.Draft, Written, null), await Chapter(chapter));

        desk.Clock.Advance(TimeSpan.FromDays(2));
        await desk.Save(author, novel, chapter, ChapterStatuses.Published);

        Assert.Equal(new Stored(ChapterStatuses.Published, Written, Written.AddDays(3)), await Chapter(chapter));
    }

    [Fact]
    public async Task Unpublishing_and_publishing_again_keep_the_first_publish_time()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Draft);
        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, chapter, ChapterStatuses.Published);
        var firstOut = Written.AddDays(1);

        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, chapter, ChapterStatuses.Draft);
        Assert.Equal(new Stored(ChapterStatuses.Draft, Written, firstOut), await Chapter(chapter));

        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, chapter, ChapterStatuses.Published);
        Assert.Equal(new Stored(ChapterStatuses.Published, Written, firstOut), await Chapter(chapter));

        // Later saves, with the status the editor resends or without one, keep it too.
        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, chapter, ChapterStatuses.Published);
        await desk.Save(author, novel, chapter, status: null);
        Assert.Equal(new Stored(ChapterStatuses.Published, Written, firstOut), await Chapter(chapter));
    }

    [Fact]
    public async Task A_chapter_created_published_keeps_its_creation_time_through_unpublishing_and_publishing_again()
    {
        var (author, novel) = await desk.SeedNovel();
        var chapter = await desk.Create(author, novel, ChapterStatuses.Published);

        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, chapter, ChapterStatuses.Draft);
        desk.Clock.Advance(TimeSpan.FromDays(1));
        await desk.Save(author, novel, chapter, ChapterStatuses.Published);

        Assert.Equal(new Stored(ChapterStatuses.Published, Written, Written), await Chapter(chapter));
    }

    private async Task<Stored> Chapter(Guid chapterId)
    {
        await using var db = database.CreateContext();
        return await db.Chapters
            .Where(c => c.Id == chapterId)
            .Select(c => new Stored(c.Status, c.CreatedAt, c.PublishedAt))
            .SingleAsync();
    }
}
