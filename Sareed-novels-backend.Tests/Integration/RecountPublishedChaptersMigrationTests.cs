using System.Reflection;
using Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// <see cref="RecountPublishedChaptersAndNotificationGifts"/> on novels as production has them: counts that included
/// drafts become the published count, right ones and nothing else change; down and up again.
/// </summary>
public class RecountPublishedChaptersMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private const string Before = "20260928155331_EarningsHoldAndReversal";
    private static readonly string Recount = typeof(RecountPublishedChaptersAndNotificationGifts).GetCustomAttribute<MigrationAttribute>()!.Id;

    private sealed record Row(Guid Id, int ChapterCount, DateTime LastUpdatedAt);

    [Fact]
    public async Task Counts_that_included_drafts_become_the_published_count_and_nothing_else_changes()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);

        var author = Seed.User();
        await Seed.InsertUserRowAsync(db, author);
        var updatedAt = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc);
        // Like «امبراطورية لانسوت»: 14 published and 2 drafts, counted 16. The others are right already (none, all
        // published), and a deleted novel is recounted too.
        var countedDrafts = Seed.Novel(author, "امبراطورية لانسوت", createdAt: updatedAt);
        var allPublished = Seed.Novel(author, "رواية منشورة", createdAt: updatedAt);
        var noChapters = Seed.Novel(author, "رواية بلا فصول", createdAt: updatedAt);
        var deleted = Seed.Novel(author, "رواية محذوفة", createdAt: updatedAt);
        deleted.IsDeleted = true;
        (countedDrafts.ChapterCount, allPublished.ChapterCount, noChapters.ChapterCount, deleted.ChapterCount) = (16, 3, 0, 2);
        db.Novels.AddRange(countedDrafts, allPublished, noChapters, deleted);
        db.Chapters.AddRange(Seed.Chapters(countedDrafts, 14, updatedAt));
        db.Chapters.AddRange(Seed.Chapters(countedDrafts, 2, updatedAt, status: "Draft", startIndex: 15));
        db.Chapters.AddRange(Seed.Chapters(allPublished, 3, updatedAt));
        db.Chapters.AddRange(Seed.Chapters(deleted, 1, updatedAt));
        db.Chapters.AddRange(Seed.Chapters(deleted, 1, updatedAt, status: "Draft", startIndex: 2));
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();
        await AssertCounts(lancelot: 14, deletedOne: 1);

        // The new notification columns take a gift and its count.
        var giftId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Notifications (Id, UserId, Type, ActorId, ActorDisplayName, Message, ActionUrl, IsRead, CreatedAt, GiftId, GiftCount)
            VALUES ({Guid.NewGuid()}, {author.Id}, N'GiftReceived', {author.Id}, N'قارئ', N'قارئ أرسل وردة ×3 إلى روايتك', N'/novel/x', 0,
                    {DateTime.UtcNow}, {giftId}, 3)
            """);
        Assert.Equal(3, await db.Database.SqlQuery<int>($"SELECT GiftCount AS Value FROM Notifications WHERE GiftId = {giftId}").SingleAsync());

        // Down counts every chapter again (the rule before), up recounts: the same.
        await migrator.MigrateAsync(Before);
        Assert.DoesNotContain(Recount, await db.Database.GetAppliedMigrationsAsync());
        await AssertCounts(lancelot: 16, deletedOne: 2);
        await migrator.MigrateAsync();
        await AssertCounts(lancelot: 14, deletedOne: 1);

        async Task AssertCounts(int lancelot, int deletedOne)
        {
            var rows = (await db.Database
                    .SqlQuery<Row>($"SELECT Id, ChapterCount, LastUpdatedAt FROM Novels WHERE AuthorId = {author.Id}")
                    .ToListAsync())
                .ToDictionary(r => r.Id);
            Assert.Equal(lancelot, rows[countedDrafts.Id].ChapterCount);
            Assert.Equal(3, rows[allPublished.Id].ChapterCount);
            Assert.Equal(0, rows[noChapters.Id].ChapterCount);
            Assert.Equal(deletedOne, rows[deleted.Id].ChapterCount);
            Assert.All(rows.Values, r => Assert.Equal(updatedAt, r.LastUpdatedAt));
        }
    }
}
