using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Domain.Moderation;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>POST /api/reports: what can be reported, by whom, how often, and what the reporter gets back.</summary>
[Collection(ReaderApiCollection.Name)]
public class ReportHttpTests(SardApiFactory api)
{
    private async Task<List<Report>> ReportsBy(ApiUser reporter)
    {
        await using var db = api.Db();
        return await db.Reports.AsNoTracking().Where(r => r.ReporterId == reporter.Id).ToListAsync();
    }

    [Fact]
    public async Task A_report_is_created_and_a_repeat_while_it_is_open_answers_200_with_the_same_report()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var (chapter, _) = await api.AddChapter(await api.AddNovel(author), "<p>فقرة</p>");
        var commentId = await api.Comment(author, $"/api/comment/chapter/{chapter.Id}", "كلام مسيء");

        var first = await api.Report(reader, "Comment", commentId, "Harassment", "يسيء إلى القرّاء");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var report = await first.OkJson();
        Assert.Equal("Comment", report.GetProperty("targetType").GetString());
        Assert.Equal(commentId, report.GetProperty("targetId").GetGuid());
        Assert.Equal("Harassment", report.GetProperty("reason").GetString());
        Assert.Equal("يسيء إلى القرّاء", report.GetProperty("details").GetString());
        Assert.Equal("Open", report.GetProperty("status").GetString());
        Assert.EndsWith("Z", report.GetProperty("createdAt").GetString());

        // Names are read ignoring case; the answer uses the canonical ones.
        var again = await api.Report(reader, "comment", commentId.ToString().ToUpperInvariant(), "spam");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var repeated = await again.OkJson();
        Assert.Equal(report.GetProperty("id").GetGuid(), repeated.GetProperty("id").GetGuid());
        Assert.Equal("Harassment", repeated.GetProperty("reason").GetString());

        var stored = Assert.Single(await ReportsBy(reader));
        Assert.Equal(author.Id, stored.TargetOwnerId);
        Assert.Equal("كلام مسيء", stored.TargetExcerpt);
        Assert.Equal(ReportStatus.Open, stored.Status);
    }

    [Fact]
    public async Task Every_kind_of_target_can_be_reported_and_keeps_its_author_and_text()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author, title: "رواية للإبلاغ");
        var (chapter, _) = await api.AddChapter(novel, "<p>فقرة</p>");
        var targets = new (string Type, Guid Id, string Excerpt)[]
        {
            ("Comment", await api.Comment(author, $"/api/comment/chapter/{chapter.Id}", "تعليق مخالف"), "تعليق مخالف"),
            ("Review", await api.Review(author, (await api.AddNovel(reader)).Id, content: "تقييم مخالف"), "تقييم مخالف"),
            ("Post", await api.Post(author, "منشور مخالف"), "منشور مخالف"),
            ("User", Guid.Parse(author.Id), $"{author.UserName} (@{author.UserName})"),
            ("Novel", novel.Id, "رواية للإبلاغ"),
        };
        var listId = await api.ReadingList(author, name: "قائمة مخالفة");

        foreach (var (type, id, _) in targets)
        {
            Assert.Equal(HttpStatusCode.Created, (await api.Report(reader, type, id, "Other")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Created, (await api.Report(reader, "ReadingList", listId, "Spam")).StatusCode);

        var stored = await ReportsBy(reader);
        Assert.Equal(6, stored.Count);
        Assert.All(stored, r => Assert.Equal(author.Id, r.TargetOwnerId));
        foreach (var (type, id, excerpt) in targets)
        {
            Assert.Equal(excerpt, stored.Single(r => r.TargetType.ToString() == type && r.TargetId == id).TargetExcerpt);
        }
        Assert.StartsWith("قائمة مخالفة", stored.Single(r => r.TargetType == ReportTargetType.ReadingList).TargetExcerpt);
    }

    [Theory]
    [InlineData("Chapter", "Spam", null, "TargetType")]
    [InlineData("0", "Spam", null, "TargetType")]
    [InlineData("Comment", "Rude", null, "Reason")]
    [InlineData("Comment", "3", null, "Reason")]
    [InlineData("Comment", null, null, "Reason")]
    [InlineData("Comment", "Spam", 1001, "Details")]
    public async Task A_report_with_an_unknown_type_or_reason_or_long_details_is_a_400_in_arabic(string? targetType, string? reason, int? detailsLength, string field)
    {
        var reader = await api.SignUp();

        var response = await api.Send(HttpMethod.Post, "/api/reports", reader, JsonContent.Create(new
        {
            targetType, targetId = Guid.NewGuid().ToString(), reason, details = detailsLength is { } n ? new string('x', n) : null
        }));

        var problem = await response.Error(HttpStatusCode.BadRequest);
        var message = Assert.Single(problem.GetProperty("errors").GetProperty(field).EnumerateArray()).GetString()!;
        Assert.Matches(@"\p{IsArabic}", message);
        Assert.Empty(await ReportsBy(reader));
    }

    [Fact]
    public async Task A_target_id_that_is_not_a_guid_is_a_400()
    {
        var reader = await api.SignUp();

        var response = await api.Report(reader, "User", "not-a-guid");

        Assert.True((await response.Error(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("TargetId", out _));
    }

    [Fact]
    public async Task Details_of_exactly_1000_characters_are_kept()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var details = new string('ت', Report.DetailsMaxLength);

        var response = await api.Report(reader, "User", author.Id, "Other", details);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(details, Assert.Single(await ReportsBy(reader)).Details);
    }

    [Fact]
    public async Task What_does_not_exist_or_the_reporter_cannot_see_is_a_404()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var (chapter, _) = await api.AddChapter(await api.AddNovel(author), "<p>فقرة</p>");
        var deletedComment = await api.Comment(author, $"/api/comment/chapter/{chapter.Id}");
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, $"/api/comment/{deletedComment}", author)).StatusCode);
        var deletedPost = await api.Post(author);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, $"/api/posts/{deletedPost}", author)).StatusCode);
        var draft = await api.AddNovel(author, isDraft: true);
        var privateList = await api.ReadingList(author, isPublic: false);

        foreach (var (type, id) in new[]
                 {
                     ("Comment", Guid.NewGuid()), ("Comment", deletedComment), ("Post", deletedPost), ("Review", Guid.NewGuid()),
                     ("User", Guid.NewGuid()), ("Novel", draft.Id), ("ReadingList", privateList)
                 })
        {
            var error = await (await api.Report(reader, type, id)).Error(HttpStatusCode.NotFound);
            Assert.Equal("TargetNotFound", error.GetProperty("code").GetString());
            Assert.Equal("المحتوى الذي تحاول الإبلاغ عنه غير موجود", error.GetProperty("message").GetString());
        }
        Assert.Empty(await ReportsBy(reader));
    }

    [Fact]
    public async Task Nobody_reports_themselves_or_their_own_content()
    {
        var author = await api.SignUp();
        var post = await api.Post(author);

        foreach (var (type, id) in new[] { ("User", (object)author.Id), ("Post", (object)post) })
        {
            var error = await (await api.Report(author, type, id)).Error(HttpStatusCode.BadRequest);
            Assert.Equal("CannotReportOwnContent", error.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Reporting_needs_a_signed_in_user()
    {
        var response = await api.Send(HttpMethod.Post, "/api/reports", null,
            JsonContent.Create(new { targetType = "User", targetId = Guid.NewGuid().ToString(), reason = "Spam" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task One_account_makes_at_most_20_reports_an_hour_and_older_ones_do_not_count()
    {
        var (busy, earlier, target) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        await using (var db = api.Db())
        {
            // 20 reports on other things: within the last hour for one reporter, two hours ago for the other.
            db.Reports.AddRange(Enumerable.Range(0, 20).SelectMany(i => new[]
            {
                StoredReport(busy, DateTime.UtcNow.AddMinutes(-50 + i)),
                StoredReport(earlier, DateTime.UtcNow.AddHours(-2).AddMinutes(i))
            }));
            await db.SaveChangesAsync();
        }

        var refused = await (await api.Report(busy, "User", target.Id)).Error(HttpStatusCode.TooManyRequests);
        Assert.Equal("TooManyReports", refused.GetProperty("code").GetString());
        Assert.Matches(@"\p{IsArabic}", refused.GetProperty("message").GetString()!);

        Assert.Equal(HttpStatusCode.Created, (await api.Report(earlier, "User", target.Id)).StatusCode);
        // A repeat of an open report is still answered: it creates nothing.
        Assert.Equal(HttpStatusCode.OK, (await api.Report(earlier, "User", target.Id)).StatusCode);
    }

    private static Report StoredReport(ApiUser reporter, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        ReporterId = reporter.Id,
        TargetType = ReportTargetType.Comment,
        TargetId = Guid.NewGuid(),
        TargetOwnerId = Guid.NewGuid().ToString(),
        Reason = ReportReason.Spam,
        Status = ReportStatus.Open,
        CreatedAt = createdAt
    };

    [Fact]
    public async Task One_address_gets_30_report_requests_per_10_minutes()
    {
        var reader = await api.SignUp();
        var client = api.ClientFrom("10.99.0.1");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 31; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/reports")
            {
                Content = JsonContent.Create(new { targetType = "Comment", targetId = Guid.NewGuid().ToString(), reason = "Spam" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reader.Token);
            statuses.Add((await client.SendAsync(request)).StatusCode);
        }

        Assert.All(statuses.Take(30), s => Assert.Equal(HttpStatusCode.NotFound, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[30]);
    }

    [Fact]
    public async Task Concurrent_repeats_of_a_report_leave_one_open_report()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => api.Report(reader, "Post", post)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.OkJson()).GetProperty("id").GetGuid()));
        Assert.Single(ids.Distinct());
        Assert.Single(await ReportsBy(reader));
    }
}
