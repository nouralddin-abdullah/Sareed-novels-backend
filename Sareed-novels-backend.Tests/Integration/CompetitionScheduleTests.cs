using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Competitions;
using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using static Domain.Entities.CompetitionStatus;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The competition status rule (#42), decided in SQL (the <c>?status=</c> filter, the active list) and in memory (the
/// status the API shows, joining, leaving), against the contract written out here on its own: for every stored status,
/// at each date of the schedule, a minute and a tick either side of it and at the instant itself.
/// </summary>
public class CompetitionScheduleTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static readonly string[] Statuses = [Upcoming, Participation, Judging, Completed];

    private static readonly TimeSpan[] Offsets =
        [TimeSpan.FromMinutes(-1), TimeSpan.FromTicks(-1), TimeSpan.Zero, TimeSpan.FromTicks(1), TimeSpan.FromMinutes(1)];

    /// <summary>
    /// #42 as the issue states it: Upcoming before the participation start date, Participation until its end date,
    /// Judging after (the judgment and results dates don't change it), and the stored status where it is further along.
    /// </summary>
    private static string Contract(string stored, DateTime participationStart, DateTime participationEnd, DateTime now)
    {
        var fromDates = now < participationStart ? Upcoming : now < participationEnd ? Participation : Judging;
        return Array.IndexOf(Statuses, stored) > Array.IndexOf(Statuses, fromDates) ? stored : fromDates;
    }

    [Fact]
    public async Task Sql_and_memory_give_the_contract_s_status_at_every_date_for_every_stored_status()
    {
        var start = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        var seeded = Statuses.Select(stored => Competition(start, stored, isActive: true))
            .Append(Competition(start, Upcoming, isActive: false))
            .Append(Competition(start, Participation, isActive: false))
            .ToList();
        await using (var db = database.CreateContext())
        {
            db.Competitions.AddRange(seeded);
            await db.SaveChangesAsync();
        }

        var ids = seeded.Select(c => c.Id).ToHashSet();
        await using var read = database.CreateContext();
        var repository = new CompetitionRepository(read);
        // As the handlers see them: read back from SQL Server (dates without a kind).
        var competitions = await read.Competitions.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync();
        var dates = new[] { start, start.AddDays(5), start.AddDays(26), start.AddDays(31), start.AddDays(38) };

        // Which competitions a query returned, by what tells them apart, so a failure reads "stored Judging, inactive".
        var labels = competitions.ToDictionary(c => c.Id, c => $"stored {c.Status}{(c.IsActive ? "" : ", inactive")}");
        string Labels(IEnumerable<Competition> found) =>
            string.Join("; ", found.Where(c => ids.Contains(c.Id)).Select(c => labels[c.Id]).Order());

        foreach (var now in dates.SelectMany(date => Offsets.Select(offset => date + offset)))
        {
            string Expected(Competition c) => Contract(c.Status, c.ParticipationStartDate, c.ParticipationEndDate, now);

            foreach (var c in competitions)
            {
                var expected = Expected(c);
                Assert.Equal((now, c.Status, c.IsActive, expected), (now, c.Status, c.IsActive, c.EffectiveStatus(now)));
                Assert.Equal((now, c.Status, c.IsActive, "join", c.IsActive && expected == Participation),
                    (now, c.Status, c.IsActive, "join", c.CanJoin(now)));
                Assert.Equal((now, c.Status, c.IsActive, "leave", expected is Upcoming or Participation),
                    (now, c.Status, c.IsActive, "leave", c.CanLeave(now)));
            }

            foreach (var status in Statuses)
            {
                Assert.Equal((now, "?status=" + status, Labels(competitions.Where(c => Expected(c) == status))),
                    (now, "?status=" + status, Labels(await repository.GetByStatusAsync(status, now))));
            }

            Assert.Equal((now, "active", Labels(competitions.Where(c => c.IsActive && Expected(c) != Completed))),
                (now, "active", Labels(await repository.GetActiveCompetitionsAsync(now))));
        }
    }

    [Fact]
    public void The_four_statuses_are_read_in_any_letter_case_and_nothing_else()
    {
        foreach (var (value, expected) in new[]
                 {
                     ("Upcoming", Upcoming), ("participation", Participation), (" JUDGING ", Judging), ("Completed", Completed)
                 })
        {
            Assert.True(CompetitionSchedule.TryParseStatus(value, out var status));
            Assert.Equal(expected, status);
        }

        foreach (var value in new[] { null, "", " ", "Finished", "Active", "Judged", "0" })
        {
            Assert.False(CompetitionSchedule.TryParseStatus(value, out _));
        }
    }

    [Fact]
    public void A_competition_s_status_is_never_mapped_without_the_time()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(CompetitionProfile).Assembly)).CreateMapper();
        var competition = Competition(new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc), Upcoming, isActive: true);

        // AutoMapper wraps what a member map throws.
        var error = Record.Exception(() => mapper.Map<CompetitionDto>(competition));
        Assert.IsType<InvalidOperationException>(error?.GetBaseException());

        var dto = mapper.MapAt<CompetitionDto>(competition, competition.ParticipationStartDate);
        Assert.Equal((Participation, true), (dto.Status, dto.CanJoin));
    }

    private static Competition Competition(DateTime start, string stored, bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        Name = "مسابقة",
        Slug = "c-" + Seed.Marker(),
        ParticipationStartDate = start,
        ParticipationEndDate = start.AddDays(5),
        JudgmentStartDate = start.AddDays(26),
        JudgmentEndDate = start.AddDays(31),
        ResultsDate = start.AddDays(38),
        Status = stored,
        IsActive = isActive,
        CreatedAt = start.AddDays(-7)
    };
}
