using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Domain.Entities.CompetitionStatus;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The API with a clock the tests set (its <see cref="TimeProvider"/>), for what follows the time: a competition's
/// status (#42). Sign-ups go through <see cref="Api"/>, on the real clock; its tokens last 60 days, so the clock can be
/// set anywhere within that.
/// </summary>
public sealed class ClockedApiFactory : IAsyncLifetime
{
    private static int nextIp;

    public SardApiFactory Api { get; } = new();

    public MutableClock Clock { get; } = new(DateTime.UtcNow);

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;

    public Task InitializeAsync()
    {
        Factory = Api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        }));
        // Started now: its startup migrates the database the tests seed.
        _ = Factory.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Api.DisposeAsync();
    }

    /// <summary>Sends a request to the clocked API, as <paramref name="user"/> when given, from an address of its own.</summary>
    public Task<HttpResponseMessage> Send(HttpMethod method, string url, ApiUser? user = null, HttpContent? content = null)
    {
        var client = Factory.CreateClient();
        var n = Interlocked.Increment(ref nextIp);
        client.DefaultRequestHeaders.Add("X-Test-Client-IP", $"10.16.{n / 250 % 250}.{n % 250 + 1}");
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (user != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        }
        return client.SendAsync(request);
    }
}

/// <summary>
/// A competition's status follows its schedule (#42), through the API as the web and the Android app use it: the list
/// and the page (<c>status</c>, <c>canJoin</c>), the <c>?status=</c> filter, my participations, joining and leaving. The
/// API's clock is set a minute before, at, and a minute after each date. The stored status is an admin's override that
/// counts only where it is further along than the dates; finalizing a competition nobody joined completes it.
/// </summary>
public class CompetitionStatusHttpTests(ClockedApiFactory clocked) : IClassFixture<ClockedApiFactory>
{
    private static readonly string[] Statuses = [Upcoming, Participation, Judging, Completed];

    /// <summary>A competition, as created (stored Upcoming unless said), an author and two of her eligible novels.</summary>
    private sealed record Scene(Competition Competition, ApiUser Author, Novel Joining, Novel Leaving);

    // Each date, and the status a minute before it, at it, and a minute after it. A date is the first instant of the
    // phase it opens; the judgment dates and the results date don't change the status: it is Judging until finalized.
    public static TheoryData<string, string, string, string> Dates => new()
    {
        { nameof(Competition.ParticipationStartDate), Upcoming, Participation, Participation },
        { nameof(Competition.ParticipationEndDate), Participation, Judging, Judging },
        { nameof(Competition.JudgmentStartDate), Judging, Judging, Judging },
        { nameof(Competition.JudgmentEndDate), Judging, Judging, Judging },
        { nameof(Competition.ResultsDate), Judging, Judging, Judging }
    };

    [Theory]
    [MemberData(nameof(Dates))]
    public async Task The_status_follows_the_dates_a_minute_before_at_and_a_minute_after_each(
        string date, string before, string at, string after)
    {
        var scene = await Setup();
        var instant = DateOf(scene.Competition, date);

        await AssertEverywhere(scene, instant.AddMinutes(-1), before);
        await AssertEverywhere(scene, instant, at);
        await AssertEverywhere(scene, instant.AddMinutes(1), after);
    }

    [Fact]
    public async Task A_contest_like_production_s_stored_upcoming_months_after_its_dates_is_judging()
    {
        // «انا مميز»: participation 25-30 Dec 2025, results 1 Feb 2026, stored Upcoming. Here nine months after its
        // results date, as the Android app's smoke test saw it (Upcoming).
        var scene = await Setup(start: new DateTime(2025, 12, 25, 14, 45, 38, 138, DateTimeKind.Utc));
        var competition = scene.Competition;
        Assert.Equal(new DateTime(2026, 2, 1, 14, 45, 38, 138), competition.ResultsDate);

        await AssertEverywhere(scene, new DateTime(2026, 9, 30, 6, 29, 12, DateTimeKind.Utc), Judging);
    }

    [Fact]
    public async Task An_admin_status_further_along_than_the_dates_overrides_them_and_the_dates_take_over_once_past_it()
    {
        var scene = await Setup();
        var competition = scene.Competition;
        var admin = await clocked.Api.SignUpAdmin();

        // Opened early: two days before its start date.
        clocked.Clock.UtcNow = competition.ParticipationStartDate.AddDays(-2);
        Assert.Equal(Participation, (await Update(admin, competition, new { status = "Participation" })).GetProperty("status").GetString());
        await AssertEverywhere(scene, competition.ParticipationStartDate.AddDays(-2), Participation);
        // The override doesn't hold it open past its end date: from then the dates are further along.
        await AssertEverywhere(scene, competition.ParticipationEndDate, Judging);

        // Closed early: a day into its participation window.
        clocked.Clock.UtcNow = competition.ParticipationStartDate.AddDays(1);
        Assert.Equal(Judging, (await Update(admin, competition, new { status = "Judging" })).GetProperty("status").GetString());
        await AssertEverywhere(scene, competition.ParticipationStartDate.AddDays(1), Judging);

        // Completed early, and completed whatever the dates say.
        Assert.Equal(Completed, (await Update(admin, competition, new { status = "Completed" })).GetProperty("status").GetString());
        await AssertEverywhere(scene, competition.ParticipationStartDate.AddDays(1), Completed);
        await AssertEverywhere(scene, competition.ParticipationStartDate.AddMinutes(-1), Completed);
        await AssertEverywhere(scene, competition.ResultsDate.AddMinutes(1), Completed);
    }

    [Fact]
    public async Task A_stored_status_behind_the_dates_counts_for_nothing_and_moving_the_dates_postpones()
    {
        var scene = await Setup();
        var competition = scene.Competition;
        var admin = await clocked.Api.SignUpAdmin();

        // In its participation window, storing Upcoming doesn't close it.
        clocked.Clock.UtcNow = competition.ParticipationStartDate.AddMinutes(1);
        Assert.Equal(Participation, (await Update(admin, competition, new { status = "Upcoming" })).GetProperty("status").GetString());
        await AssertEverywhere(scene, competition.ParticipationStartDate.AddMinutes(1), Participation);

        // After it, storing Participation doesn't reopen it.
        clocked.Clock.UtcNow = competition.ParticipationEndDate.AddMinutes(1);
        Assert.Equal(Judging, (await Update(admin, competition, new { status = "Participation" })).GetProperty("status").GetString());
        await AssertEverywhere(scene, competition.ParticipationEndDate.AddMinutes(1), Judging);
        Assert.Equal(Participation, await StoredStatus(competition));

        // To postpone, the admin moves the dates (and drops the override): a new window from tomorrow.
        var newStart = competition.ParticipationEndDate.AddDays(1);
        var postponed = await Update(admin, competition, new
        {
            participationStartDate = newStart,
            participationEndDate = newStart.AddDays(5),
            status = "Upcoming"
        });
        Assert.Equal(Upcoming, postponed.GetProperty("status").GetString());
        await AssertEverywhere(scene, competition.ParticipationEndDate.AddMinutes(1), Upcoming);
        await AssertEverywhere(scene, newStart, Participation);
    }

    [Fact]
    public async Task Finalizing_a_competition_nobody_joined_completes_it_without_winners_and_again_changes_nothing()
    {
        var competition = await AddCompetition(StartInTwoDays());
        var admin = await clocked.Api.SignUpAdmin();

        // Past its results date with nobody in it, it is Judging until finalized.
        clocked.Clock.UtcNow = competition.ResultsDate.AddDays(1);
        Assert.Equal(Judging, (await Page(competition)).GetProperty("status").GetString());

        foreach (var attempt in new[] { "first", "again" })
        {
            var finalize = await clocked.Send(HttpMethod.Post, $"/api/competition/{competition.Id}/finalize", admin);
            Assert.Equal((attempt, 0), (attempt, (await finalize.OkJson()).GetArrayLength()));

            var page = await Page(competition);
            Assert.Equal((attempt, Completed, false, 0), (attempt, page.GetProperty("status").GetString(),
                page.GetProperty("canJoin").GetBoolean(), page.GetProperty("winners").GetArrayLength()));
            Assert.True(await Listed(competition, "?status=Completed"));
            Assert.False(await Listed(competition, "?status=Judging"));
        }
        Assert.Equal(Completed, await StoredStatus(competition));

        // Completed whatever the dates say, even back in its participation window: nobody can join.
        clocked.Clock.UtcNow = competition.ParticipationStartDate.AddMinutes(1);
        Assert.Equal(Completed, (await Page(competition)).GetProperty("status").GetString());
        var author = await clocked.Api.SignUp();
        var novel = await EligibleNovel(author);
        await Refused(await Join(author, competition, novel), "CompetitionClosed", "join a completed competition");
    }

    [Fact]
    public async Task Only_the_four_statuses_filter_the_list_or_are_stored_and_participation_must_end_after_it_starts()
    {
        var competition = await AddCompetition(StartInTwoDays());
        var admin = await clocked.Api.SignUpAdmin();
        clocked.Clock.UtcNow = competition.ParticipationEndDate.AddMinutes(1);

        // The filter takes the four names in any letter case; blank lists them all; anything else is refused.
        Assert.True(await Listed(competition, "?status=judging"));
        Assert.True(await Listed(competition, "?status=%20Judging%20"));
        Assert.True(await Listed(competition, "?status="));
        await Refused(await clocked.Send(HttpMethod.Get, "/api/competition?status=Finished"), "InvalidStatus", "filter by Finished",
            HttpStatusCode.BadRequest);

        // An admin's status is stored as written in CompetitionStatus; anything else is refused and stores nothing.
        Assert.Equal(Completed, (await Update(admin, competition, new { status = "completed" })).GetProperty("status").GetString());
        Assert.Equal(Completed, await StoredStatus(competition));
        foreach (var status in new[] { "Finished", "", "Active" })
        {
            var update = await clocked.Send(HttpMethod.Put, $"/api/competition/{competition.Id}", admin,
                JsonContent.Create(new { status, name = "لا يتغير" }));
            await Refused(update, "InvalidStatus", $"store status '{status}'", HttpStatusCode.BadRequest);
        }
        Assert.Equal(Completed, await StoredStatus(competition));

        // Participation must end after it starts, when creating or moving it.
        var start = competition.ParticipationStartDate;
        var moved = await clocked.Send(HttpMethod.Put, $"/api/competition/{competition.Id}", admin,
            JsonContent.Create(new { participationEndDate = start }));
        await Refused(moved, "InvalidSchedule", "end participation when it starts", HttpStatusCode.BadRequest);
        var created = await clocked.Send(HttpMethod.Post, "/api/competition", admin, JsonContent.Create(new
        {
            name = "Backwards",
            participationStartDate = start,
            participationEndDate = start.AddDays(-1),
            judgmentStartDate = start,
            judgmentEndDate = start,
            resultsDate = start
        }));
        await Refused(created, "InvalidSchedule", "create a competition ending before it starts", HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// At <paramref name="time"/>, everything the apps read says <paramref name="expected"/>, and joining and leaving
    /// go by it: a novel joins only in Participation (403 CompetitionClosed otherwise) and leaves until participation
    /// ends (403 ParticipationEnded after).
    /// </summary>
    private async Task AssertEverywhere(Scene scene, DateTime time, string expected)
    {
        clocked.Clock.UtcNow = time;
        var competition = scene.Competition;
        var open = expected == Participation;
        var leavable = expected is Upcoming or Participation;

        var listed = (await (await clocked.Send(HttpMethod.Get, "/api/competition")).OkJson()).EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == competition.Id);
        Assert.Equal((time, "list", expected, open),
            (time, "list", listed.GetProperty("status").GetString(), listed.GetProperty("canJoin").GetBoolean()));

        var page = await Page(competition);
        Assert.Equal((time, "page", expected, open),
            (time, "page", page.GetProperty("status").GetString(), page.GetProperty("canJoin").GetBoolean()));

        foreach (var status in Statuses)
        {
            Assert.Equal((time, "?status=" + status, status == expected),
                (time, "?status=" + status, await Listed(competition, "?status=" + status)));
        }

        await SetParticipating(competition, scene.Leaving, true);
        await SetParticipating(competition, scene.Joining, false);
        var mine = (await (await clocked.Send(HttpMethod.Get, "/api/competition/my-participations", scene.Author)).OkJson())
            .EnumerateArray().Single(p => p.GetProperty("competitionId").GetGuid() == competition.Id);
        Assert.Equal((time, "my-participations", expected),
            (time, "my-participations", mine.GetProperty("competitionStatus").GetString()));

        var join = await Join(scene.Author, competition, scene.Joining);
        if (open)
        {
            Assert.Equal((time, "join", HttpStatusCode.OK), (time, "join", join.StatusCode));
        }
        else
        {
            await Refused(join, "CompetitionClosed", $"join at {time:O}");
        }
        Assert.Equal((time, "joined", open), (time, "joined", await IsParticipating(competition, scene.Joining)));

        var leave = await clocked.Send(HttpMethod.Delete, $"/api/competition/{competition.Id}/leave/{scene.Leaving.Id}", scene.Author);
        if (leavable)
        {
            Assert.Equal((time, "leave", HttpStatusCode.OK), (time, "leave", leave.StatusCode));
        }
        else
        {
            await Refused(leave, "ParticipationEnded", $"leave at {time:O}");
        }
        Assert.Equal((time, "left", leavable), (time, "left", !await IsParticipating(competition, scene.Leaving)));
    }

    private async Task<Scene> Setup(DateTime? start = null)
    {
        var competition = await AddCompetition(start ?? StartInTwoDays());
        var author = await clocked.Api.SignUp();
        return new Scene(competition, author, await EligibleNovel(author), await EligibleNovel(author));
    }

    /// <summary>
    /// Two days from now at 10:00 and a fraction of a second, so "at the instant" tests the exact tick. The whole
    /// schedule ends within six weeks, inside the 60 days the tokens last.
    /// </summary>
    private static DateTime StartInTwoDays() => DateTime.UtcNow.Date.AddDays(2).AddHours(10).AddTicks(1_234_567);

    /// <summary>
    /// A competition as created, stored Upcoming, with its dates spaced like production's «انا مميز»: five days of
    /// participation from <paramref name="start"/>, judgment from three weeks later for five days, results a week after.
    /// </summary>
    private async Task<Competition> AddCompetition(DateTime start)
    {
        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "مسابقة " + Seed.Marker(),
            Slug = "c-" + Seed.Marker(),
            TotalPrize = 100, PrizeFirstPlace = 40, PrizeSecondPlace = 35, PrizeThirdPlace = 25,
            ParticipationStartDate = start,
            ParticipationEndDate = start.AddDays(5),
            JudgmentStartDate = start.AddDays(26),
            JudgmentEndDate = start.AddDays(31),
            ResultsDate = start.AddDays(38),
            MinChapters = 1,
            Status = Upcoming,
            CreatedAt = DateTime.UtcNow
        };
        await using var db = clocked.Api.Db();
        db.Competitions.Add(competition);
        await db.SaveChangesAsync();
        return competition;
    }

    /// <summary>A published novel by <paramref name="author"/> with a published chapter: what joining asks for.</summary>
    private async Task<Novel> EligibleNovel(ApiUser author)
    {
        var novel = await clocked.Api.AddNovel(author);
        await clocked.Api.AddChapter(novel, "<p>فصل</p>");
        return novel;
    }

    private static DateTime DateOf(Competition competition, string date) => date switch
    {
        nameof(Competition.ParticipationStartDate) => competition.ParticipationStartDate,
        nameof(Competition.ParticipationEndDate) => competition.ParticipationEndDate,
        nameof(Competition.JudgmentStartDate) => competition.JudgmentStartDate,
        nameof(Competition.JudgmentEndDate) => competition.JudgmentEndDate,
        nameof(Competition.ResultsDate) => competition.ResultsDate,
        _ => throw new ArgumentOutOfRangeException(nameof(date), date, null)
    };

    private async Task<JsonElement> Page(Competition competition) =>
        await (await clocked.Send(HttpMethod.Get, $"/api/competition/{competition.Slug}")).OkJson();

    private async Task<bool> Listed(Competition competition, string query) =>
        (await (await clocked.Send(HttpMethod.Get, "/api/competition" + query)).OkJson()).EnumerateArray()
        .Any(c => c.GetProperty("id").GetGuid() == competition.Id);

    private Task<HttpResponseMessage> Join(ApiUser author, Competition competition, Novel novel) =>
        clocked.Send(HttpMethod.Post, $"/api/competition/{competition.Id}/join", author, JsonContent.Create(new { novelId = novel.Id }));

    private async Task<JsonElement> Update(ApiUser admin, Competition competition, object body) =>
        await (await clocked.Send(HttpMethod.Put, $"/api/competition/{competition.Id}", admin, JsonContent.Create(body))).OkJson();

    private async Task<string> StoredStatus(Competition competition)
    {
        await using var db = clocked.Api.Db();
        return await db.Competitions.Where(c => c.Id == competition.Id).Select(c => c.Status).SingleAsync();
    }

    private async Task<bool> IsParticipating(Competition competition, Novel novel)
    {
        await using var db = clocked.Api.Db();
        return await db.CompetitionParticipants.AnyAsync(p => p.CompetitionId == competition.Id && p.NovelId == novel.Id);
    }

    /// <summary>Puts the novel in the competition, or takes it out, directly.</summary>
    private async Task SetParticipating(Competition competition, Novel novel, bool participating)
    {
        await using var db = clocked.Api.Db();
        var rows = db.CompetitionParticipants.Where(p => p.CompetitionId == competition.Id && p.NovelId == novel.Id);
        if (!participating)
        {
            await rows.ExecuteDeleteAsync();
        }
        else if (!await rows.AnyAsync())
        {
            db.CompetitionParticipants.Add(new CompetitionParticipant
            {
                Id = Guid.NewGuid(), CompetitionId = competition.Id, NovelId = novel.Id, JoinedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>A refusal with this status and code, and an Arabic message for the user.</summary>
    private static async Task Refused(HttpResponseMessage response, string code, string what,
        HttpStatusCode status = HttpStatusCode.Forbidden)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status && body.Contains($"\"code\":\"{code}\""),
            $"{what}: expected {(int)status} {code}, got {(int)response.StatusCode}: {body}");
        Assert.Matches(@"\p{IsArabic}", JsonDocument.Parse(body).RootElement.GetProperty("message").GetString()!);
    }
}
