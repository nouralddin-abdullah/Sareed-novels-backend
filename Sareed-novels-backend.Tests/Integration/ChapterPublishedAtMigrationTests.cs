using System.Reflection;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// <see cref="AddChapterPublishedAt"/> on chapters as production has them: a published chapter came out at the first
/// new-chapter notification sent for it, or at its creation when there is none; an unpublished chapter that was
/// notified keeps that time; a draft never published stays null. Running the backfill again changes nothing; down
/// drops the column and keeps the chapters, up fills it in again.
/// </summary>
public class ChapterPublishedAtMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private static readonly string Stamp = typeof(AddChapterPublishedAt).GetCustomAttribute<MigrationAttribute>()!.Id;

    private sealed record Row(Guid Id, DateTime? PublishedAt);

    private sealed record StatusRow(Guid Id, string Status);

    [Fact]
    public async Task Chapters_come_out_at_their_first_new_chapter_notification_or_their_creation_and_drafts_stay_null()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        var before = migrations[migrations.IndexOf(Stamp) - 1];
        await migrator.MigrateAsync(before);

        var author = Seed.User();
        var readers = new[] { Seed.User(), Seed.User() };
        foreach (var user in readers.Prepend(author))
        {
            await Seed.InsertUserRowAsync(db, user);
        }
        var written = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var novel = Seed.Novel(author, "رواية منشورة", createdAt: written);
        db.Novels.Add(novel);
        await db.SaveChangesAsync();

        // Written a minute apart from 09:01; as the API saved chapters before this migration, without PublishedAt.
        var chapters = Seed.Chapters(novel, 5, written);
        var (createdPublished, publishedLater, nobodyNotified, unpublished, neverPublished) =
            (chapters[0], chapters[1], chapters[2], chapters[3], chapters[4]);
        unpublished.Status = ChapterStatuses.Draft;
        neverPublished.Status = ChapterStatuses.Draft;
        await Seed.InsertChapterRowsAsync(db, chapters);

        // The new-chapter notifications NotificationService sent, a batch per publish.
        var createdNotified = createdPublished.CreatedAt.AddSeconds(1);
        await Notify(createdPublished, readers[0], createdNotified.AddMilliseconds(3));
        await Notify(createdPublished, readers[1], createdNotified);
        // A draft published three days after it was written, then unpublished and published again: notified twice.
        var cameOut = publishedLater.CreatedAt.AddDays(3);
        await Notify(publishedLater, readers[1], cameOut.AddDays(2));
        await Notify(publishedLater, readers[0], cameOut);
        await Notify(publishedLater, readers[1], cameOut.AddMilliseconds(2));
        // Notified when it came out, unpublished since.
        var unpublishedCameOut = unpublished.CreatedAt.AddDays(1);
        await Notify(unpublished, readers[0], unpublishedCameOut);
        // Only new-chapter notifications tell when a chapter came out: another kind naming the chapter doesn't.
        await Notify(nobodyNotified, readers[0], nobodyNotified.CreatedAt.AddDays(5), NotificationType.CommentOnChapter);
        await Notify(neverPublished, readers[0], neverPublished.CreatedAt.AddDays(5), NotificationType.CommentOnChapter);

        await migrator.MigrateAsync(Stamp);

        var expected = new Dictionary<Guid, DateTime?>
        {
            [createdPublished.Id] = createdNotified,
            [publishedLater.Id] = cameOut,
            [nobodyNotified.Id] = nobodyNotified.CreatedAt,
            [unpublished.Id] = unpublishedCameOut,
            [neverPublished.Id] = null
        };
        Assert.Equal(expected, await PublishedAt());

        // Again: nothing left to fill in, nothing changes.
        Assert.Equal(0, await db.Database.ExecuteSqlRawAsync(AddChapterPublishedAt.Backfill));
        Assert.Equal(expected, await PublishedAt());

        // Down drops the column and keeps the chapters as they were; up fills it in the same way again.
        await migrator.MigrateAsync(before);
        Assert.DoesNotContain(Stamp, await db.Database.GetAppliedMigrationsAsync());
        Assert.Null(await db.Database
            .SqlQuery<int?>($"SELECT CAST(COL_LENGTH('Chapters', 'PublishedAt') AS int) AS Value")
            .SingleAsync());
        Assert.Equal(
            chapters.Select(c => new StatusRow(c.Id, c.Status)).OrderBy(r => r.Id),
            (await db.Database.SqlQuery<StatusRow>($"SELECT Id, Status FROM Chapters WHERE NovelId = {novel.Id}").ToListAsync()).OrderBy(r => r.Id));
        await migrator.MigrateAsync(Stamp);
        Assert.Equal(expected, await PublishedAt());

        async Task<Dictionary<Guid, DateTime?>> PublishedAt() => (await db.Database
                .SqlQuery<Row>($"SELECT Id, PublishedAt FROM Chapters WHERE NovelId = {novel.Id}")
                .ToListAsync())
            .ToDictionary(r => r.Id, r => r.PublishedAt);

        Task Notify(Chapter chapter, User reader, DateTime at, string type = NotificationType.NewChapterInLibrary) =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Notifications (Id, UserId, Type, ActorId, ActorDisplayName, ActorProfilePhoto, Message, ActionUrl,
                                           IsRead, CreatedAt, RelatedEntityId, RelatedEntityType)
                VALUES ({Guid.NewGuid()}, {reader.Id}, {type}, {novel.Id.ToString()}, {novel.Title}, {novel.CoverImageUrl},
                        {$"فصل جديد في «{novel.Title}»: {chapter.Title}"}, {$"/novel/{novel.Slug}/chapter/{chapter.Id}"}, 0,
                        {at}, {chapter.Id}, N'Chapter')
                """);
    }
}
