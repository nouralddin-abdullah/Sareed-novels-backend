using System.Reflection;
using Domain.Entities;
using Infrastructure.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// <see cref="EarlyAccessByChapter"/> on early access as production had it (#94): a chapter that readers found locked
/// stays locked, from the deploy, and a free one stays free (in production, 2026-10-06, all 5 novels with early access
/// had nothing locked); every novel with early access gets 7 days; from then on a novel has days or subscribers only,
/// never both nor neither.
/// </summary>
public class EarlyAccessByChapterMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private const string Before = "20261005060931_AddChapterWordsCountAndPublishAt";
    private static readonly string Migration = typeof(EarlyAccessByChapter).GetCustomAttribute<MigrationAttribute>()!.Id;

    private sealed record ChapterRow(Guid Id, DateTime? EarlyAccessFrom, DateTime? EarlyAccessFreedAt);

    private sealed record PrivilegeRow(Guid NovelId, int? EarlyAccessDays, bool SubscribersOnly);

    [Fact]
    public async Task What_readers_found_locked_stays_locked_from_the_deploy_and_nothing_else_locks()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);

        var author = Seed.User();
        await Seed.InsertUserRowAsync(db, author);
        var at = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var (window, nothing, off, belowEleven) =
            (Seed.Novel(author, "نافذة"), Seed.Novel(author, "لا شيء مقفل"), Seed.Novel(author, "مطفأ"), Seed.Novel(author, "قبل الحادي عشر"));
        db.Novels.AddRange(window, nothing, off, belowEleven);
        await db.SaveChangesAsync();

        // A window of 3 from position 12 of 14; like production, nothing locked (start after the last, count 0); early
        // access off; and a start that slid below 11 (a chapter before it was deleted).
        var chapters = new Dictionary<Novel, List<Chapter>>
        {
            [window] = Published(window, 14, at),
            [nothing] = Published(nothing, 25, at),
            [off] = Published(off, 15, at),
            [belowEleven] = Published(belowEleven, 12, at)
        };
        var draft = Seed.Chapters(window, 1, at, status: "Draft", startIndex: 15);
        await Seed.InsertChapterRowsAsync(db, [.. chapters.Values.SelectMany(c => c), .. draft], withPublishedAt: true);
        await InsertPrivilege(db, window, enabled: true, lockedCount: 3, start: 12, at);
        await InsertPrivilege(db, nothing, enabled: true, lockedCount: 0, start: 26, at);
        await InsertPrivilege(db, off, enabled: false, lockedCount: 5, start: 11, at);
        await InsertPrivilege(db, belowEleven, enabled: true, lockedCount: 4, start: 9, at);

        var before = DateTime.UtcNow.AddSeconds(-1);
        await migrator.MigrateAsync(Migration);
        var after = DateTime.UtcNow.AddSeconds(1);
        await AssertConverted();

        // Neither days nor subscribers only, or both, is refused from now on.
        foreach (var (days, only) in new[] { ((int?)null, false), (7, true), (31, false), (0, false) })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE NovelPrivileges SET EarlyAccessDays = {days}, SubscribersOnly = {only} WHERE NovelId = {window.Id}"));
            Assert.Equal(547, error.Number);
        }

        // Down, then up again: the same.
        await migrator.MigrateAsync(Before);
        Assert.DoesNotContain(Migration, await db.Database.GetAppliedMigrationsAsync());
        before = DateTime.UtcNow.AddSeconds(-1);
        await migrator.MigrateAsync(Migration);
        after = DateTime.UtcNow.AddSeconds(1);
        await AssertConverted();

        async Task AssertConverted()
        {
            var privileges = await db.Database
                .SqlQuery<PrivilegeRow>($"SELECT NovelId, EarlyAccessDays, SubscribersOnly FROM NovelPrivileges")
                .ToListAsync();
            Assert.All(privileges, p => Assert.Equal((7, false), (p.EarlyAccessDays, p.SubscribersOnly)));
            Assert.Equal(4, privileges.Count);

            var stored = (await db.Database
                    .SqlQuery<ChapterRow>($"SELECT Id, EarlyAccessFrom, EarlyAccessFreedAt FROM Chapters")
                    .ToListAsync())
                .ToDictionary(c => c.Id);
            var locked = chapters[window].Skip(11).Concat(chapters[belowEleven].Skip(10)).Select(c => c.Id).ToHashSet();
            Assert.Equal(5, locked.Count); // 12-14, and 11-12 (never 9 or 10: the first 10 stay free)
            foreach (var chapter in chapters.Values.SelectMany(c => c).Concat(draft))
            {
                var row = stored[chapter.Id];
                Assert.Null(row.EarlyAccessFreedAt);
                if (locked.Contains(chapter.Id))
                {
                    Assert.InRange(row.EarlyAccessFrom!.Value, before, after);
                }
                else
                {
                    Assert.Null(row.EarlyAccessFrom);
                }
            }
        }
    }

    private static List<Chapter> Published(Novel novel, int count, DateTime at)
    {
        var chapters = Seed.Chapters(novel, count, at);
        chapters.ForEach(c => c.PublishedChapterSequence = c.ChapterIndex);
        return chapters;
    }

    private static Task InsertPrivilege(DbContext db, Novel novel, bool enabled, int lockedCount, int start, DateTime at) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO NovelPrivileges (Id, NovelId, IsEnabled, MaxLockedChapters, SubscriptionCost, CurrentLockedCount,
                PrivilegeStartSequence, LastDailyUnlockDate, TotalDailyUnlocksPerformed, MinPublishedRequired, CreatedAt, UpdatedAt)
            VALUES ({Guid.NewGuid()}, {novel.Id}, {enabled}, 20, 100, {lockedCount}, {start}, NULL, 0, 11, {at}, NULL)
            """);
}
