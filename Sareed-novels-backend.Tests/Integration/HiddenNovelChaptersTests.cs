using Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #80: while a novel is hidden from readers (a draft novel), a chapter of it that comes out tells nobody, however it
/// comes out: created published, published by its author, or on schedule (#77). Everything else a chapter coming out
/// does happens as for any novel. Publishing the novel later doesn't announce those chapters; its next chapter is.
/// </summary>
public class HiddenNovelChaptersTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>, IAsyncLifetime
{
    private static readonly DateTime Start = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    private readonly ScheduleDesk desk = new(database, Start);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await desk.DisposeAsync();

    [Fact]
    public async Task A_hidden_novels_chapters_come_out_without_telling_its_readers_on_every_path()
    {
        var (author, novel) = await desk.SeedNovel(hidden: true);

        var created = await desk.Create(author, novel, ChapterStatuses.Published);
        var draft = await desk.Create(author, novel, ChapterStatuses.Draft);
        var scheduled = await desk.Create(author, novel, ChapterStatuses.Draft, publishAt: Start.AddHours(1));
        desk.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True((await desk.Save(author, novel, draft, ChapterStatuses.Published)).Success);
        desk.Clock.Advance(TimeSpan.FromMinutes(30));
        await desk.PublishDue();

        foreach (var chapter in new[] { created, draft, scheduled })
        {
            var stored = await desk.Stored(chapter);
            Assert.Equal(ChapterStatuses.Published, stored.Status);
            Assert.NotNull(stored.PublishedAt);
            Assert.Equal(0, await desk.Announced(chapter, expected: 0));
        }

        // The rest of coming out happens as for any novel: its chapter count, last update and privilege window.
        var hidden = await desk.StoredNovel(novel.Id);
        Assert.Equal((3, desk.Clock.UtcNow), (hidden.ChapterCount, hidden.LastUpdatedAt));
        Assert.Equal(3, desk.WindowExtensions(novel.Id));
    }

    [Fact]
    public async Task Publishing_the_novel_later_announces_none_of_them_and_its_next_chapter_is_announced()
    {
        var (author, novel) = await desk.SeedNovel(hidden: true);
        var whileHidden = await desk.Create(author, novel, ChapterStatuses.Published);

        await using (var db = database.CreateContext())
        {
            await db.Novels.Where(n => n.Id == novel.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDraft, false));
        }
        var afterwards = await desk.Create(author, novel, ChapterStatuses.Published);

        Assert.Equal(1, await desk.Announced(afterwards));
        Assert.Equal(0, await desk.Announced(whileHidden, expected: 0));

        // Unpublished and published again, it isn't new either.
        Assert.True((await desk.Save(author, novel, whileHidden, ChapterStatuses.Draft)).Success);
        Assert.True((await desk.Save(author, novel, whileHidden, ChapterStatuses.Published)).Success);
        Assert.Equal(0, await desk.Announced(whileHidden, expected: 0));
    }
}
