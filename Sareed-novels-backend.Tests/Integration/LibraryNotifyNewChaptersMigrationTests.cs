using System.Reflection;
using Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// <see cref="AddLibraryNotifyNewChapters"/> on libraries as production has them: every existing entry keeps its
/// new-chapter notifications (the column's default is 1), nothing else changes; down and up again.
/// </summary>
public class LibraryNotifyNewChaptersMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private static readonly string Mute = typeof(AddLibraryNotifyNewChapters).GetCustomAttribute<MigrationAttribute>()!.Id;

    private sealed record Row(string UserId, Guid LastReadChapterId, int LastReadChapterNumber, DateTime LastReadAt);

    [Fact]
    public async Task Existing_library_entries_keep_their_notifications_and_nothing_else_changes()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        var before = migrations[migrations.IndexOf(Mute) - 1];
        await migrator.MigrateAsync(before);

        var author = Seed.User();
        var readers = new[] { Seed.User(), Seed.User() };
        foreach (var user in readers.Prepend(author))
        {
            await Seed.InsertUserRowAsync(db, user);
        }
        var readAt = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc);
        var novel = Seed.Novel(author, "رواية في مكتبتين", createdAt: readAt);
        var chapters = Seed.Chapters(novel, 2, readAt);
        db.Novels.Add(novel);
        db.Chapters.AddRange(chapters);
        await db.SaveChangesAsync();
        // As the API inserted entries before this migration: without the column.
        for (var i = 0; i < readers.Length; i++)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO UserNovelProgress (UserId, NovelId, LastReadChapterId, LastReadChapterNumber, LastReadAt, CreatedAt)
                VALUES ({readers[i].Id}, {novel.Id}, {chapters[i].Id}, {i + 1}, {readAt.AddHours(i)}, {readAt})
                """);
        }

        await migrator.MigrateAsync(Mute);

        Assert.Equal([true, true], await Notify());
        await AssertRowsUnchanged();

        // Down drops the column and keeps the entries; up again, they notify again.
        await migrator.MigrateAsync(before);
        Assert.DoesNotContain(Mute, await db.Database.GetAppliedMigrationsAsync());
        Assert.Null(await db.Database
            .SqlQuery<int?>($"SELECT CAST(COL_LENGTH('UserNovelProgress', 'NotifyNewChapters') AS int) AS Value")
            .SingleAsync());
        await AssertRowsUnchanged();
        await migrator.MigrateAsync(Mute);
        Assert.Equal([true, true], await Notify());

        async Task<List<bool>> Notify() => await db.Database
            .SqlQuery<bool>($"SELECT NotifyNewChapters AS Value FROM UserNovelProgress WHERE NovelId = {novel.Id} ORDER BY LastReadChapterNumber")
            .ToListAsync();

        async Task AssertRowsUnchanged()
        {
            var rows = await db.Database
                .SqlQuery<Row>($"SELECT UserId, LastReadChapterId, LastReadChapterNumber, LastReadAt FROM UserNovelProgress WHERE NovelId = {novel.Id}")
                .ToListAsync();
            Assert.Equal(
                readers.Select((r, i) => new Row(r.Id, chapters[i].Id, i + 1, readAt.AddHours(i))).OrderBy(r => r.LastReadChapterNumber),
                rows.OrderBy(r => r.LastReadChapterNumber));
        }
    }
}
