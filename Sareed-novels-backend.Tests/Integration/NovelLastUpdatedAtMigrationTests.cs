using System.Reflection;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// <see cref="RecomputeNovelLastUpdatedAt"/> on novels as the rule before #39 left them (LastUpdatedAt moved to each
/// chapter's creation, drafts included, and not when a draft was published): each gets when its newest chapter came
/// out, one unpublished since included, or its creation when none has. Running it again changes nothing; down puts the
/// rule before back, up the new one again.
/// </summary>
public class NovelLastUpdatedAtMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private static readonly string Stamp =
        typeof(RecomputeNovelLastUpdatedAt).GetCustomAttribute<MigrationAttribute>()!.Id;

    private sealed record Row(Guid Id, DateTime LastUpdatedAt);

    [Fact]
    public async Task Novels_were_last_updated_when_their_newest_chapter_came_out_or_when_they_were_created()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        var before = migrations[migrations.IndexOf(Stamp) - 1];
        await migrator.MigrateAsync(before);

        var author = Seed.User();
        await Seed.InsertUserRowAsync(db, author);
        var day0 = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var chaptersOf = new Dictionary<Novel, List<Chapter>>();

        // Chapters created published, the newest on day 3: right under both rules.
        var upToDate = Novel("up to date");
        Chapters(upToDate, (day0.AddDays(1), day0.AddDays(1)), (day0.AddDays(3), day0.AddDays(3)));
        // A chapter out on day 2, then a draft written on day 6 that moved it.
        var draftAfter = Novel("draft after");
        Chapters(draftAfter, (day0.AddDays(2), day0.AddDays(2)), (day0.AddDays(6), null));
        // A draft written on day 1 (which moved it) and published on day 9 (which didn't).
        var publishedLater = Novel("published later");
        Chapters(publishedLater, (day0.AddDays(1), day0.AddDays(9)));
        // Only drafts: nothing has come out since it was created on day 0.
        var draftsOnly = Novel("drafts only");
        Chapters(draftsOnly, (day0.AddDays(4), null), (day0.AddDays(5), null));
        // Out on day 7 and unpublished since: it still came out then.
        var unpublished = Novel("unpublished");
        var unpublishedChapters = Chapters(
            unpublished, (day0.AddDays(2), day0.AddDays(2)), (day0.AddDays(7), day0.AddDays(7)));
        unpublishedChapters[1].Status = ChapterStatuses.Draft;
        // No chapters.
        var empty = Novel("empty");
        var novels = new[] { upToDate, draftAfter, publishedLater, draftsOnly, unpublished, empty };
        foreach (var novel in novels)
        {
            // What the rule before #39 left: its creation, moved to each chapter's creation.
            novel.LastUpdatedAt = chaptersOf[novel].Select(c => c.CreatedAt).Append(novel.CreatedAt).Max();
        }
        var ruleBefore = novels.ToDictionary(n => n.Id, n => n.LastUpdatedAt);
        await db.SaveChangesAsync();
        // The chapters as the schema at that point has them (the model's later columns aren't there yet).
        await Seed.InsertChapterRowsAsync(db, chaptersOf.Values.SelectMany(c => c), withPublishedAt: true);

        await migrator.MigrateAsync(Stamp);

        var expected = new Dictionary<Guid, DateTime>
        {
            [upToDate.Id] = day0.AddDays(3),
            [draftAfter.Id] = day0.AddDays(2),
            [publishedLater.Id] = day0.AddDays(9),
            [draftsOnly.Id] = day0,
            [unpublished.Id] = day0.AddDays(7),
            [empty.Id] = day0
        };
        Assert.Equal(expected, await LastUpdated());
        Assert.Equal(ruleBefore[upToDate.Id], expected[upToDate.Id]);

        // Again: nothing left to change.
        Assert.Equal(0, await db.Database.ExecuteSqlRawAsync(RecomputeNovelLastUpdatedAt.Recompute));
        Assert.Equal(expected, await LastUpdated());

        // Down puts the rule before back; up the new one again.
        await migrator.MigrateAsync(before);
        Assert.Equal(ruleBefore, await LastUpdated());
        await migrator.MigrateAsync(Stamp);
        Assert.Equal(expected, await LastUpdated());

        async Task<Dictionary<Guid, DateTime>> LastUpdated()
        {
            var ids = novels.Select(n => n.Id).ToList();
            return (await db.Database
                    .SqlQuery<Row>($"SELECT Id, LastUpdatedAt FROM Novels")
                    .ToListAsync())
                .Where(r => ids.Contains(r.Id))
                .ToDictionary(r => r.Id, r => r.LastUpdatedAt);
        }

        Novel Novel(string title)
        {
            var novel = Seed.Novel(author, title, createdAt: day0);
            db.Novels.Add(novel);
            chaptersOf[novel] = [];
            return novel;
        }

        // Chapters written at the first time of each pair and out at the second (null: never published).
        List<Chapter> Chapters(Novel novel, params (DateTime Written, DateTime? Out)[] chapters)
        {
            var rows = Seed.Chapters(novel, chapters.Length, day0);
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].CreatedAt = chapters[i].Written;
                rows[i].PublishedAt = chapters[i].Out;
                rows[i].Status = chapters[i].Out == null ? ChapterStatuses.Draft : ChapterStatuses.Published;
            }
            chaptersOf[novel].AddRange(rows);
            return rows;
        }
    }
}
