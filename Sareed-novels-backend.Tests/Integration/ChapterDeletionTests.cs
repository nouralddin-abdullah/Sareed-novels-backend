using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>Deleting a chapter must not trip over readers whose "stopped at" points at it.</summary>
public class ChapterDeletionTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private async Task<(User Reader, List<Chapter> Chapters)> SeedChapters(params string[] statuses)
    {
        await using var db = database.CreateContext();
        var author = Seed.User();
        var reader = Seed.User();
        reader.LibraryNovelsCount = 1;
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        var chapters = Seed.Chapters(novel, statuses.Length, DateTime.UtcNow.AddDays(-5));
        for (var i = 0; i < chapters.Count; i++)
        {
            chapters[i].Status = statuses[i];
        }

        db.Users.AddRange(author, reader);
        db.Novels.Add(novel);
        db.Chapters.AddRange(chapters);
        await db.SaveChangesAsync();
        return (reader, chapters);
    }

    private async Task StopAt(User reader, Chapter chapter)
    {
        await using var db = database.CreateContext();
        db.UserNovelProgress.Add(Seed.Progress(reader, chapter, chapter.ChapterIndex, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task Delete(Chapter chapter)
    {
        await using var db = database.CreateContext();
        var repository = new ChaptersRepository(db);
        var tracked = await repository.GetChapterById(chapter.Id);
        Assert.True(await repository.DeleteChapter(tracked!));
    }

    [Fact]
    public async Task Readers_move_back_to_the_nearest_published_chapter_before_it()
    {
        var (reader, chapters) = await SeedChapters("Published", "Draft", "Published", "Published");
        await StopAt(reader, chapters[2]);

        await Delete(chapters[2]);

        await using var check = database.CreateContext();
        Assert.False(await check.Chapters.AnyAsync(c => c.Id == chapters[2].Id));
        var progress = await check.UserNovelProgress.SingleAsync(p => p.UserId == reader.Id);
        Assert.Equal(chapters[0].Id, progress.LastReadChapterId);
        Assert.Equal(1, progress.LastReadChapterNumber);
    }

    [Fact]
    public async Task Readers_of_the_first_chapter_move_forward_to_the_next_published_one()
    {
        var (reader, chapters) = await SeedChapters("Published", "Published", "Published");
        await StopAt(reader, chapters[0]);

        await Delete(chapters[0]);

        await using var check = database.CreateContext();
        var progress = await check.UserNovelProgress.SingleAsync(p => p.UserId == reader.Id);
        Assert.Equal(chapters[1].Id, progress.LastReadChapterId);
        Assert.Equal(1, progress.LastReadChapterNumber);
    }

    [Fact]
    public async Task Deleting_a_novel_s_only_chapter_removes_the_progress_and_its_library_count()
    {
        var (reader, chapters) = await SeedChapters("Published");
        await StopAt(reader, chapters[0]);

        await Delete(chapters[0]);

        await using var check = database.CreateContext();
        Assert.False(await check.UserNovelProgress.AnyAsync(p => p.UserId == reader.Id));
        Assert.Equal(0, (await check.Users.SingleAsync(u => u.Id == reader.Id)).LibraryNovelsCount);
    }

    [Fact]
    public async Task Chapters_nobody_stopped_at_delete_as_before()
    {
        var (reader, chapters) = await SeedChapters("Published", "Published");
        await StopAt(reader, chapters[0]);

        await Delete(chapters[1]);

        await using var check = database.CreateContext();
        Assert.Equal(chapters[0].Id, (await check.UserNovelProgress.SingleAsync(p => p.UserId == reader.Id)).LastReadChapterId);
        Assert.False(await check.Chapters.AnyAsync(c => c.Id == chapters[1].Id));
    }
}
