using System.Text.Json;
using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.Scheduling;
using Application.Extensions;
using Domain.Constants;
using Infrastructure.BackgroundJobs;
using Infrastructure.Extensions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>#77: only a draft is scheduled, only for a time to come, and the time a client sends is read as UTC.</summary>
public class ChapterScheduleTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_draft_is_scheduled_for_a_time_to_come()
    {
        Assert.Null(ChapterSchedule.Refusal(Now.AddMinutes(1), ChapterStatuses.Draft, Now));
        Assert.Null(ChapterSchedule.Refusal(Now.AddDays(30), ChapterStatuses.Draft, Now));
    }

    [Fact]
    public void Now_or_a_past_time_is_refused_in_arabic()
    {
        foreach (var at in new[] { Now, Now.AddSeconds(-1), Now.AddDays(-3) })
        {
            var refusal = ChapterSchedule.Refusal(at, ChapterStatuses.Draft, Now);
            Assert.Equal(new ScheduleRefusal(ChapterSchedule.InPastCode, "موعد النشر يجب أن يكون في المستقبل"), refusal);
        }
    }

    [Fact]
    public void A_chapter_that_is_or_becomes_published_is_not_scheduled()
    {
        var refusal = ChapterSchedule.Refusal(Now.AddHours(1), ChapterStatuses.Published, Now);
        Assert.Equal(new ScheduleRefusal(ChapterSchedule.NotDraftCode, "يمكن تحديد موعد نشر للمسودات فقط"), refusal);
    }

    [Fact]
    public void Null_schedules_nothing_and_is_never_refused()
    {
        Assert.Null(ChapterSchedule.Refusal(null, ChapterStatuses.Draft, Now));
        Assert.Null(ChapterSchedule.Refusal(null, ChapterStatuses.Published, Now));
    }

    [Theory]
    [InlineData("\"2026-10-05T15:00:00Z\"", "2026-10-05T15:00:00")]
    [InlineData("\"2026-10-05T18:00:00+03:00\"", "2026-10-05T15:00:00")]
    [InlineData("\"2026-10-05T15:00:00\"", "2026-10-05T15:00:00")]
    public void A_time_with_Z_or_an_offset_is_converted_and_one_without_is_UTC(string json, string utc)
    {
        // As ASP.NET reads a request body.
        var sent = JsonSerializer.Deserialize<DateTime?>(json, JsonSerializerOptions.Web);

        var stored = ChapterSchedule.ToUtc(sent)!.Value;

        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(DateTime.Parse(utc), DateTime.SpecifyKind(stored, DateTimeKind.Unspecified));
    }

    [Fact]
    public void The_app_runs_the_scheduler_and_the_backfill_and_publishes_a_novels_due_chapters_before_reading_it()
    {
        // The HTTP tests' app leaves the two jobs out (SardApiFactory), so here is where the app's own wiring is checked.
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        services.AddApplication();

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ScheduledChapterPublishingService));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ChapterWordsBackfillService));
        Assert.Contains(services, d => d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(PublishDueChaptersBehavior<,>));
        Assert.Contains(services, d => d.ServiceType == typeof(ScheduledChapterPublisher) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Theory]
    [InlineData("""{ "title": "فصل" }""", false, null)]
    [InlineData("""{ "title": "فصل", "publishAt": null }""", true, null)]
    [InlineData("""{ "title": "فصل", "publishAt": "2026-10-06T08:00:00Z" }""", true, "2026-10-06T08:00:00")]
    public void An_edit_tells_a_left_out_schedule_from_a_cancelled_one(string body, bool sent, string? at)
    {
        var request = JsonSerializer.Deserialize<UpdateChapterRequest>(body, JsonSerializerOptions.Web)!;

        DateTime? expected = at is null ? null : DateTime.Parse(at);
        DateTime? read = ChapterSchedule.ToUtc(request.PublishAt) is { } utc ? DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) : null;
        Assert.Equal(sent, request.PublishAtSent);
        Assert.Equal(expected, read);
    }
}
