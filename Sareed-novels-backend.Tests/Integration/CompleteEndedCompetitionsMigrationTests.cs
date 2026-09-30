using System.Reflection;
using Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static Domain.Entities.CompetitionStatus;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// <see cref="CompleteEndedCompetitionsWithoutParticipants"/> (#42): a competition whose participation has ended and
/// whose results date has passed, with nobody in it, is completed, as production's «انا مميز» needs. One with
/// participants waits for an admin to finalize it (and choose its winners), one not ended is left to its dates, one
/// completed stays as it is. Running it again changes nothing; down leaves them completed.
/// </summary>
public class CompleteEndedCompetitionsMigrationTests(EmptySqlServerDatabase database) : IClassFixture<EmptySqlServerDatabase>
{
    private static readonly string Stamp =
        typeof(CompleteEndedCompetitionsWithoutParticipants).GetCustomAttribute<MigrationAttribute>()!.Id;

    private sealed record Row(Guid Id, string Status, DateTime? UpdatedAt);

    [Fact]
    public async Task Completes_the_ended_competitions_nobody_joined_and_leaves_the_others()
    {
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        var before = migrations[migrations.IndexOf(Stamp) - 1];
        await migrator.MigrateAsync(before);

        // Rows as the tables have them at this point, in plain SQL, so later migrations to these tables don't break it.
        var author = Seed.User();
        await Seed.InsertUserRowAsync(db, author);
        var novel = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Novels (Id, AuthorId, Title, Slug, Summary, CoverImageUrl, Status, CreatedAt, LastUpdatedAt, TotalViews)
            VALUES ({novel}, {author.Id}, N'رواية', {"n-" + Seed.Marker()}, N'ملخص', N'https://example.test/cover.png',
                N'Ongoing', {author.CreatedAt}, {author.CreatedAt}, 0)
            """);

        var now = DateTime.UtcNow;
        var edited = new DateTime(2026, 1, 5, 12, 0, 0);
        // Production's «انا مميز»: participation 25-30 Dec 2025, results 1 Feb 2026, stored Upcoming, nobody in it.
        var imSpecial = await Competition(Upcoming, new DateTime(2025, 12, 25, 14, 45, 38, 138), resultsAfter: TimeSpan.FromDays(38));
        // Also nobody in it, closed early by an admin (stored Judging).
        var closedEarly = await Competition(Judging, now.AddDays(-20), resultsAfter: TimeSpan.FromDays(10), updatedAt: edited);
        // Ended with a participant: judging until an admin finalizes it.
        var withParticipant = await Competition(Upcoming, now.AddDays(-20), resultsAfter: TimeSpan.FromDays(10));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO CompetitionParticipants (Id, CompetitionId, NovelId, JoinedAt)
            VALUES ({Guid.NewGuid()}, {withParticipant}, {novel}, {now.AddDays(-19)})
            """);
        // Participation over, results due tomorrow: judging.
        var resultsTomorrow = await Competition(Upcoming, now.AddDays(-10), resultsAfter: TimeSpan.FromDays(11));
        // Results date passed but participation still open (a schedule out of order): left to its dates.
        var stillOpen = await Competition(Participation, now.AddDays(-2), resultsAfter: TimeSpan.FromDays(1));
        // Not started.
        var upcoming = await Competition(Upcoming, now.AddDays(3), resultsAfter: TimeSpan.FromDays(38));
        // Already completed (finalized), long ago.
        var finalized = await Competition(Completed, now.AddDays(-60), resultsAfter: TimeSpan.FromDays(38), updatedAt: edited);
        var ruleBefore = await Rows();

        await migrator.MigrateAsync(Stamp);
        var migrated = DateTime.UtcNow;

        var after = await Rows();
        foreach (var completed in new[] { imSpecial, closedEarly })
        {
            Assert.Equal(Completed, after[completed].Status);
            Assert.InRange(after[completed].UpdatedAt!.Value, now.AddMinutes(-5), migrated.AddMinutes(5));
        }
        foreach (var untouched in new[] { withParticipant, resultsTomorrow, stillOpen, upcoming, finalized })
        {
            Assert.Equal(ruleBefore[untouched], after[untouched]);
        }

        // Again: nothing left to complete.
        Assert.Equal(0, await db.Database.ExecuteSqlRawAsync(CompleteEndedCompetitionsWithoutParticipants.Complete));
        Assert.Equal(after, await Rows());

        // Down leaves them completed (what an ended competition shows under the rule before #42 too); up again changes
        // nothing.
        await migrator.MigrateAsync(before);
        Assert.Equal(after, await Rows());
        await migrator.MigrateAsync(Stamp);
        Assert.Equal(after, await Rows());

        async Task<Dictionary<Guid, Row>> Rows() =>
            (await db.Database.SqlQuery<Row>($"SELECT Id, Status, UpdatedAt FROM Competitions").ToListAsync())
            .ToDictionary(r => r.Id);

        // A competition with five days of participation from start and results that long after it starts.
        async Task<Guid> Competition(string status, DateTime start, TimeSpan resultsAfter, DateTime? updatedAt = null)
        {
            var id = Guid.NewGuid();
            var end = start.AddDays(5);
            var results = start + resultsAfter;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Competitions (Id, Name, Slug, TotalPrize, PrizeFirstPlace, PrizeSecondPlace, PrizeThirdPlace,
                    ParticipationStartDate, ParticipationEndDate, JudgmentStartDate, JudgmentEndDate, ResultsDate,
                    MinChapters, Status, IsActive, CreatedAt, UpdatedAt)
                VALUES ({id}, N'مسابقة', {"c-" + Seed.Marker()}, 100, 40, 35, 25, {start}, {end}, {end}, {results}, {results},
                    5, {status}, 1, {start.AddDays(-6)}, {updatedAt})
                """);
            return id;
        }
    }
}
