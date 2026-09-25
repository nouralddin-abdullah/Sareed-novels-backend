using Domain.Entities;
using Domain.Library;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

public class LibraryRepositoryTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private async Task<(User Author, User Reader)> SeedUsers()
    {
        await using var db = database.CreateContext();
        var author = Seed.User();
        var reader = Seed.User();
        db.Users.AddRange(author, reader);
        await db.SaveChangesAsync();
        return (author, reader);
    }

    private async Task<(Novel Novel, List<Chapter> Chapters)> SeedNovel(User author, int chapters = 3, bool isDraft = false, bool isDeleted = false)
    {
        await using var db = database.CreateContext();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker(), isDraft);
        novel.IsDeleted = isDeleted;
        var list = Seed.Chapters(novel, chapters, DateTime.UtcNow.AddDays(-10));
        for (var i = 0; i < list.Count; i++)
        {
            list[i].PublishedChapterSequence = i + 1;
        }

        db.Novels.Add(novel);
        db.Chapters.AddRange(list);
        await db.SaveChangesAsync();
        return (novel, list);
    }

    private async Task AddProgress(User reader, Chapter chapter, int number, DateTime lastReadAt)
    {
        await using var db = database.CreateContext();
        db.UserNovelProgress.Add(Seed.Progress(reader, chapter, number, lastReadAt));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_library_leaves_out_draft_and_deleted_novels_and_its_total_matches_the_pages()
    {
        var (author, reader) = await SeedUsers();
        var older = await SeedNovel(author);
        var newer = await SeedNovel(author);
        var draft = await SeedNovel(author, isDraft: true);
        var deleted = await SeedNovel(author, isDeleted: true);
        await AddProgress(reader, older.Chapters[0], 1, DateTime.UtcNow.AddDays(-2));
        await AddProgress(reader, newer.Chapters[1], 2, DateTime.UtcNow.AddDays(-1));
        await AddProgress(reader, draft.Chapters[0], 1, DateTime.UtcNow);
        await AddProgress(reader, deleted.Chapters[0], 1, DateTime.UtcNow);

        await using var db = database.CreateContext();
        var repository = new LibraryRepository(db);
        var (firstPage, total) = await repository.GetUserLibraryAsync(reader.Id, 1, 1);
        var (secondPage, _) = await repository.GetUserLibraryAsync(reader.Id, 2, 1);

        Assert.Equal(2, total);
        Assert.Equal(newer.Novel.Id, Assert.Single(firstPage).NovelId);
        Assert.Equal(older.Novel.Id, Assert.Single(secondPage).NovelId);

        var entry = firstPage[0];
        Assert.Equal(newer.Chapters[1].Id, entry.LastReadChapter.Id);
        Assert.Equal(author.UserName, entry.AuthorUserName);
        Assert.Equal(newer.Chapters.Select(c => c.Id), entry.PublishedChapters.Select(c => c.Id));
    }

    [Fact]
    public async Task An_entry_whose_chapter_was_unpublished_resumes_at_the_previous_published_chapter()
    {
        var (author, reader) = await SeedUsers();
        var (novel, chapters) = await SeedNovel(author, chapters: 4);
        await AddProgress(reader, chapters[2], 3, DateTime.UtcNow);
        await using (var db = database.CreateContext())
        {
            await db.Chapters.Where(c => c.Id == chapters[2].Id || c.Id == chapters[3].Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "Draft"));
        }

        await using var check = database.CreateContext();
        var entry = await new LibraryRepository(check).GetLibraryEntryAsync(reader.Id, novel.Id);

        Assert.NotNull(entry);
        var resume = ReadingPosition.Resolve(entry.LastReadChapter, entry.PublishedChapters);
        Assert.Equal(chapters[1].Id, resume.ChapterId);
        Assert.Equal(2, resume.ChapterNumber);
        Assert.Equal(100m, resume.ProgressPercentage);
    }

    [Fact]
    public async Task A_first_read_adds_the_novel_once_even_when_requests_race()
    {
        var (author, reader) = await SeedUsers();
        var (novel, chapters) = await SeedNovel(author);

        var added = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = database.CreateContext();
            return await new LibraryRepository(db).SaveProgressAsync(reader.Id, novel.Id, chapters[0].Id, 1, DateTime.UtcNow);
        }));

        await using var check = database.CreateContext();
        Assert.Equal(1, added.Count(a => a));
        Assert.Equal(1, await check.UserNovelProgress.CountAsync(p => p.UserId == reader.Id && p.NovelId == novel.Id));
        Assert.Equal(1, (await check.Users.SingleAsync(u => u.Id == reader.Id)).LibraryNovelsCount);
    }

    [Fact]
    public async Task Later_reads_update_the_row_and_reading_an_earlier_chapter_moves_it_back()
    {
        var (author, reader) = await SeedUsers();
        var (novel, chapters) = await SeedNovel(author);

        await using (var db = database.CreateContext())
        {
            var repository = new LibraryRepository(db);
            Assert.True(await repository.SaveProgressAsync(reader.Id, novel.Id, chapters[2].Id, 3, DateTime.UtcNow.AddMinutes(-5)));
            Assert.False(await repository.SaveProgressAsync(reader.Id, novel.Id, chapters[0].Id, 1, DateTime.UtcNow));
        }

        await using var check = database.CreateContext();
        var progress = await check.UserNovelProgress.SingleAsync(p => p.UserId == reader.Id && p.NovelId == novel.Id);
        Assert.Equal(chapters[0].Id, progress.LastReadChapterId);
        Assert.Equal(1, progress.LastReadChapterNumber);
        Assert.Equal(1, (await check.Users.SingleAsync(u => u.Id == reader.Id)).LibraryNovelsCount);
    }
}
