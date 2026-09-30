using System.Text.RegularExpressions;
using Domain.Entities;
using Domain.Library;
using Infrastructure.Persistence;
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

    private async Task<User> SeedUser()
    {
        await using var db = database.CreateContext();
        var user = Seed.User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Reads a chapter the way track-progress records it (the first read counts the novel in the library).</summary>
    private async Task Read(User reader, Chapter chapter, int number)
    {
        await using var db = database.CreateContext();
        await new LibraryRepository(db).SaveProgressAsync(reader.Id, chapter.NovelId, chapter.Id, number, DateTime.UtcNow);
    }

    private async Task<T> WithRepository<T>(Func<LibraryRepository, Task<T>> action)
    {
        await using var db = database.CreateContext();
        return await action(new LibraryRepository(db));
    }

    /// <summary>When a chapter was written (CreatedAt) and when it came out (PublishedAt).</summary>
    private static Task SetTimes(ApplicationDbContext db, Chapter chapter, DateTime written, DateTime cameOut) =>
        db.Chapters.Where(c => c.Id == chapter.Id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.CreatedAt, written)
            .SetProperty(c => c.PublishedAt, cameOut));

    private async Task<List<UserNovelProgress>> EntriesOf(params User[] readers)
    {
        var ids = readers.Select(r => r.Id).ToList();
        await using var db = database.CreateContext();
        return await db.UserNovelProgress.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToListAsync();
    }

    private async Task<int> LibraryCount(User reader)
    {
        await using var db = database.CreateContext();
        return await db.Users.Where(u => u.Id == reader.Id).Select(u => u.LibraryNovelsCount).SingleAsync();
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
    // #33: removing a novel from the library, muting its new chapters, and when its newest chapter came out.

    [Fact]
    public async Task Removing_a_novel_deletes_only_that_readers_entry_and_its_count_in_her_library()
    {
        var (author, reader) = await SeedUsers();
        var other = await SeedUser();
        var (kept, keptChapters) = await SeedNovel(author);
        var (removed, removedChapters) = await SeedNovel(author);
        await Read(reader, keptChapters[0], 1);
        await Read(reader, removedChapters[1], 2);
        await Read(other, removedChapters[0], 1);

        Assert.True(await WithRepository(r => r.RemoveFromLibraryAsync(reader.Id, removed.Id)));

        var entries = await EntriesOf(reader, other);
        Assert.Equal(
            new[] { (reader.Id, kept.Id), (other.Id, removed.Id) }.Order(),
            entries.Select(e => (e.UserId, e.NovelId)).Order());
        Assert.Equal(1, await LibraryCount(reader));
        Assert.Equal(1, await LibraryCount(other));
        // The other reader's entry is as it was.
        var others = Assert.Single(entries, e => e.UserId == other.Id);
        Assert.Equal((removedChapters[0].Id, true), (others.LastReadChapterId, others.NotifyNewChapters));
    }

    [Fact]
    public async Task Removing_a_novel_not_in_the_library_changes_nothing()
    {
        var (author, reader) = await SeedUsers();
        var (novel, chapters) = await SeedNovel(author);
        var (neverRead, _) = await SeedNovel(author);
        await Read(reader, chapters[0], 1);

        Assert.False(await WithRepository(r => r.RemoveFromLibraryAsync(reader.Id, neverRead.Id)));
        Assert.False(await WithRepository(r => r.RemoveFromLibraryAsync(reader.Id, Guid.NewGuid())));

        Assert.Equal(novel.Id, Assert.Single(await EntriesOf(reader)).NovelId);
        Assert.Equal(1, await LibraryCount(reader));
    }

    [Fact]
    public async Task Removals_at_once_delete_the_entry_and_count_it_out_once()
    {
        var (author, reader) = await SeedUsers();
        var (novel, chapters) = await SeedNovel(author);
        var (other, otherChapters) = await SeedNovel(author);
        await Read(reader, chapters[0], 1);
        await Read(reader, otherChapters[0], 1);

        var removed = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            WithRepository(r => r.RemoveFromLibraryAsync(reader.Id, novel.Id)))));

        Assert.Equal(1, removed.Count(r => r));
        Assert.Equal(other.Id, Assert.Single(await EntriesOf(reader)).NovelId);
        Assert.Equal(1, await LibraryCount(reader));
    }

    [Fact]
    public async Task Muting_sets_one_entry_and_a_novel_not_in_the_library_is_refused_without_adding_it()
    {
        var (author, reader) = await SeedUsers();
        var other = await SeedUser();
        var (novel, chapters) = await SeedNovel(author);
        var (neverRead, _) = await SeedNovel(author);
        await Read(reader, chapters[0], 1);
        await Read(other, chapters[0], 1);

        Assert.True(await WithRepository(r => r.SetNewChapterNotificationsAsync(reader.Id, novel.Id, false)));
        Assert.False(Assert.Single(await EntriesOf(reader)).NotifyNewChapters);
        Assert.True(Assert.Single(await EntriesOf(other)).NotifyNewChapters);

        // Off again: the entry is there, so it is done, not refused.
        Assert.True(await WithRepository(r => r.SetNewChapterNotificationsAsync(reader.Id, novel.Id, false)));
        Assert.False(await WithRepository(r => r.SetNewChapterNotificationsAsync(reader.Id, neverRead.Id, false)));
        Assert.Equal(novel.Id, Assert.Single(await EntriesOf(reader)).NovelId);

        Assert.True(await WithRepository(r => r.SetNewChapterNotificationsAsync(reader.Id, novel.Id, true)));
        Assert.True(Assert.Single(await EntriesOf(reader)).NotifyNewChapters);
    }

    [Fact]
    public async Task A_new_chapter_notifies_the_readers_with_the_novel_in_their_library_except_those_who_muted_it()
    {
        var (author, notified) = await SeedUsers();
        var (mutedByEf, muted, unmutedAgain, readsAnother) = (await SeedUser(), await SeedUser(), await SeedUser(), await SeedUser());
        var (novel, chapters) = await SeedNovel(author);
        var (another, anotherChapters) = await SeedNovel(author);
        await Read(notified, chapters[0], 1);
        await Read(muted, chapters[1], 2);
        await Read(unmutedAgain, chapters[0], 1);
        await Read(readsAnother, anotherChapters[0], 1);
        // Saved through EF with false: stored false (the column's default is true).
        await using (var db = database.CreateContext())
        {
            var entry = Seed.Progress(mutedByEf, chapters[2], 3, DateTime.UtcNow);
            entry.NotifyNewChapters = false;
            db.UserNovelProgress.Add(entry);
            await db.SaveChangesAsync();
        }
        Assert.True(await WithRepository(r => r.SetNewChapterNotificationsAsync(muted.Id, novel.Id, false)));
        Assert.True(await WithRepository(r => r.SetNewChapterNotificationsAsync(unmutedAgain.Id, novel.Id, false)));
        Assert.True(await WithRepository(r => r.SetNewChapterNotificationsAsync(unmutedAgain.Id, novel.Id, true)));

        var recipients = await WithRepository(r => r.GetUsersWithNovelInLibrary(novel.Id));

        Assert.Equal(new[] { notified.Id, unmutedAgain.Id }.Order(), recipients.Order());
        Assert.False(Assert.Single(await EntriesOf(mutedByEf)).NotifyNewChapters);
        Assert.Equal([readsAnother.Id], await WithRepository(r => r.GetUsersWithNovelInLibrary(another.Id)));
    }

    [Fact]
    public async Task Reading_a_removed_novel_again_adds_it_back_with_notifications_on()
    {
        var (author, reader) = await SeedUsers();
        var (novel, chapters) = await SeedNovel(author);
        await Read(reader, chapters[1], 2);
        Assert.True(await WithRepository(r => r.SetNewChapterNotificationsAsync(reader.Id, novel.Id, false)));
        Assert.True(await WithRepository(r => r.RemoveFromLibraryAsync(reader.Id, novel.Id)));
        Assert.Empty(await EntriesOf(reader));
        Assert.Equal(0, await LibraryCount(reader));

        Assert.True(await WithRepository(r => r.SaveProgressAsync(reader.Id, novel.Id, chapters[0].Id, 1, DateTime.UtcNow)));

        var entry = Assert.Single(await EntriesOf(reader));
        Assert.Equal((chapters[0].Id, true), (entry.LastReadChapterId, entry.NotifyNewChapters));
        Assert.Equal(1, await LibraryCount(reader));
        Assert.Equal([reader.Id], await WithRepository(r => r.GetUsersWithNovelInLibrary(novel.Id)));
    }

    [Fact]
    public async Task An_entry_says_when_the_newest_published_chapter_came_out_and_null_when_none_is_published()
    {
        var (author, reader) = await SeedUsers();
        var (novel, chapters) = await SeedNovel(author, chapters: 3);
        var (emptied, emptiedChapters) = await SeedNovel(author, chapters: 2);
        var newest = new DateTime(2026, 9, 20, 10, 3, 0, DateTimeKind.Utc);
        await using (var db = database.CreateContext())
        {
            // The newest to come out is chapter 2, a draft published after it was written, not the last in reading
            // order and not the newest written: chapter 3 was written after it and came out before it.
            await SetTimes(db, chapters[0], written: newest.AddDays(-3), cameOut: newest.AddDays(-3));
            await SetTimes(db, chapters[1], written: newest.AddDays(-2), cameOut: newest);
            await SetTimes(db, chapters[2], written: newest.AddDays(-1), cameOut: newest.AddDays(-1));
            // Neither counts: a newer draft, and a chapter unpublished after it came out (it keeps that time).
            var draft = Seed.Chapters(novel, 1, newest.AddDays(1), status: "Draft", startIndex: 4).Single();
            var unpublished = Seed.Chapters(novel, 1, newest.AddDays(1), startIndex: 5).Single();
            unpublished.Status = "Draft";
            db.Chapters.AddRange(draft, unpublished);
            await db.SaveChangesAsync();
        }
        await Read(reader, chapters[0], 1);
        await Read(reader, emptiedChapters[0], 1);
        await using (var db = database.CreateContext())
        {
            // Unpublished after she read it: still in her library, with nothing published (the chapters keep their
            // publish times, which no longer count).
            await db.Chapters.Where(c => c.NovelId == emptied.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "Draft"));
        }

        var (entries, total) = await WithRepository(r => r.GetUserLibraryAsync(reader.Id, 1, 10));

        Assert.Equal(2, total);
        var withChapters = Assert.Single(entries, e => e.NovelId == novel.Id);
        Assert.Equal(newest, withChapters.LastChapterPublishedAt);
        Assert.True(withChapters.NotifyNewChapters);
        var withoutChapters = Assert.Single(entries, e => e.NovelId == emptied.Id);
        Assert.Null(withoutChapters.LastChapterPublishedAt);
        Assert.Empty(withoutChapters.PublishedChapters);
        Assert.Equal(newest, (await WithRepository(r => r.GetLibraryEntryAsync(reader.Id, novel.Id)))!.LastChapterPublishedAt);
        Assert.Null((await WithRepository(r => r.GetLibraryEntryAsync(reader.Id, emptied.Id)))!.LastChapterPublishedAt);
    }

    [Fact]
    public async Task New_chapters_are_counted_in_the_pages_own_query_however_many_novels_it_has()
    {
        var (author, reader) = await SeedUsers();
        var readerOfOne = await SeedUser();
        var novels = new List<(Novel Novel, List<Chapter> Chapters)>();
        for (var i = 0; i < 4; i++)
        {
            novels.Add(await SeedNovel(author, chapters: 3));
        }
        // Chapters 1-3 of each novel came out a minute apart. She read novel n when all but its n newest were out
        // (novel 0: the instant its newest came out, so none is new; novel 3: a minute before its first).
        for (var n = 0; n < novels.Count; n++)
        {
            var chapters = novels[n].Chapters;
            await AddProgress(reader, chapters[0], 1, n < 3 ? chapters[2 - n].PublishedAt!.Value : chapters[0].PublishedAt!.Value.AddMinutes(-1));
        }
        await AddProgress(readerOfOne, novels[1].Chapters[0], 1, novels[1].Chapters[1].PublishedAt!.Value);

        async Task<(IReadOnlyList<LibraryEntry> Entries, CommandLog Log)> Page(User user)
        {
            var log = new CommandLog();
            await using var db = database.CreateContext(log);
            var (entries, _) = await new LibraryRepository(db).GetUserLibraryAsync(user.Id, 1, 20);
            return (entries, log);
        }

        var (four, fourLog) = await Page(reader);
        var (one, oneLog) = await Page(readerOfOne);

        Assert.Equal(novels.Select((n, i) => (n.Novel.Id, i)).Order(), four.Select(e => (e.NovelId, e.NewChaptersCount)).Order());
        Assert.Equal(1, Assert.Single(one).NewChaptersCount);
        Assert.All(four, e => Assert.Equal(e.LastChapterPublishedAt > e.LastReadAt, e.NewChaptersCount > 0));
        // The total, the page and the chapters' outlines, as before newChaptersCount and whatever the page's size: the
        // count is in the page's own query, next to when the newest chapter came out, not a query per novel.
        Assert.Equal(3, fourLog.Commands.Count);
        Assert.Equal(3, oneLog.Commands.Count);
        Assert.Single(fourLog.Commands, sql => sql.Contains("MAX(") && Regex.IsMatch(sql, @"\[PublishedAt\] > \[\w+\]\.\[LastReadAt\]"));

        // One novel's entry: its row and its outlines.
        var entryLog = new CommandLog();
        await using var entryDb = database.CreateContext(entryLog);
        Assert.Equal(2, (await new LibraryRepository(entryDb).GetLibraryEntryAsync(reader.Id, novels[2].Novel.Id))!.NewChaptersCount);
        Assert.Equal(2, entryLog.Commands.Count);
    }
}
