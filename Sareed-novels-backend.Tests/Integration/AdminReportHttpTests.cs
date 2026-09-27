using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Domain.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The moderators' side of reports: the list (GET /api/admin/reports) and the actions (PATCH /api/admin/reports/{id}),
/// which close every open report on the target and delete content the way its author does, counters included. Its own
/// API and database, so the lists hold only these tests' reports.
/// </summary>
public class AdminReportHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private static JsonElement Item(JsonElement page, Guid reportId) =>
        page.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == reportId);

    private async Task<Report> Stored(Guid reportId)
    {
        await using var db = api.Db();
        return await db.Reports.AsNoTracking().SingleAsync(r => r.Id == reportId);
    }

    [Theory]
    [InlineData("GET", "/api/admin/reports")]
    [InlineData("PATCH", "/api/admin/reports/5b8f0f3e-3f5e-4a6f-9d3c-0d6c2a6e1a11")]
    [InlineData("DELETE", "/api/admin/users/some-user/suspension")]
    public async Task Moderation_endpoints_need_an_admin(string method, string url)
    {
        var body = JsonContent.Create(new { action = "Dismiss" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Send(new HttpMethod(method), url, null, body)).StatusCode);
        var user = await api.SignUp();
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Send(new HttpMethod(method), url, user, JsonContent.Create(new { action = "Dismiss" }))).StatusCode);
    }

    [Fact]
    public async Task The_list_shows_each_report_with_its_target_author_reporter_and_open_reports_on_it()
    {
        var admin = await api.SignUpAdmin();
        var (author, first, second) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var (chapter, _) = await api.AddChapter(novel, "<p>فقرة</p>");
        var comment = await api.Comment(author, $"/api/comment/chapter/{chapter.Id}", "تعليق مسيء");
        var firstReport = await api.Reported(first, "Comment", comment, "Harassment");
        var secondReport = await api.Reported(second, "Comment", comment, "Spam");

        var page = await api.AdminReports(admin, "Open");

        // Oldest first: the queue.
        var ids = page.Ids();
        Assert.True(ids.IndexOf(firstReport) < ids.IndexOf(secondReport));
        var item = Item(page, firstReport);
        Assert.Equal("Harassment", item.GetProperty("reason").GetString());
        Assert.Equal("Open", item.GetProperty("status").GetString());
        Assert.Equal(2, item.GetProperty("openReportsOnTarget").GetInt32());
        Assert.Equal(first.Id, item.GetProperty("reporter").GetProperty("userId").GetString());
        Assert.Equal(first.UserName, item.GetProperty("reporter").GetProperty("userName").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("action").ValueKind);
        var target = item.GetProperty("target");
        Assert.Equal("Comment", target.GetProperty("type").GetString());
        Assert.Equal(comment, target.GetProperty("id").GetGuid());
        Assert.False(target.GetProperty("isDeleted").GetBoolean());
        Assert.Equal("تعليق مسيء", target.GetProperty("excerpt").GetString());
        Assert.Equal("تعليق مسيء", target.GetProperty("currentExcerpt").GetString());
        Assert.Equal($"/novel/{novel.Slug}/chapter/{chapter.Id}", target.GetProperty("link").GetString());
        var owner = target.GetProperty("owner");
        Assert.Equal(author.Id, owner.GetProperty("userId").GetString());
        Assert.False(owner.GetProperty("isSuspended").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await api.Get("/api/admin/reports?status=Closed", admin)).StatusCode);
    }

    [Fact]
    public async Task A_report_on_content_deleted_since_still_lists_with_what_it_said()
    {
        var admin = await api.SignUpAdmin();
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var post = await api.Post(author, "منشور سيحذفه صاحبه");
        var report = await api.Reported(reader, "Post", post);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, $"/api/posts/{post}", author)).StatusCode);

        var target = Item(await api.AdminReports(admin), report).GetProperty("target");

        Assert.True(target.GetProperty("isDeleted").GetBoolean());
        Assert.Equal("منشور سيحذفه صاحبه", target.GetProperty("excerpt").GetString());
        Assert.Equal(JsonValueKind.Null, target.GetProperty("currentExcerpt").ValueKind);
        Assert.Equal(JsonValueKind.Null, target.GetProperty("link").ValueKind);
        Assert.Equal(author.Id, target.GetProperty("owner").GetProperty("userId").GetString());
    }

    [Fact]
    public async Task Dismiss_closes_every_open_report_on_the_target_and_leaves_it_alone()
    {
        var admin = await api.SignUpAdmin();
        var (author, first, second) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);
        var report = await api.Reported(first, "Post", post);
        var other = await api.Reported(second, "Post", post);

        var result = await (await api.Resolve(admin, report, "Dismiss")).OkJson();

        Assert.Equal(2, result.GetProperty("resolvedReports").GetInt32());
        Assert.False(result.GetProperty("contentRemoved").GetBoolean());
        foreach (var id in new[] { report, other })
        {
            var stored = await Stored(id);
            Assert.Equal(ReportStatus.Dismissed, stored.Status);
            Assert.Equal(ReportAction.Dismiss, stored.Resolution);
            Assert.Equal(admin.Id, stored.ResolvedById);
            Assert.NotNull(stored.ResolvedAt);
        }
        Assert.DoesNotContain(report, (await api.AdminReports(admin, "Open")).Ids());
        Assert.Equal("Dismiss", Item(await api.AdminReports(admin, "Dismissed"), report).GetProperty("action").GetString());
        Assert.Contains(report, (await api.AdminReports(admin, "All")).Ids());
        Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/posts/{post}")).StatusCode);

        // The reporter can report it again: the earlier report is closed.
        Assert.Equal(HttpStatusCode.Created, (await api.Report(first, "Post", post)).StatusCode);
    }

    [Fact]
    public async Task Removing_a_comment_deletes_its_replies_likes_and_notifications_and_keeps_the_counters_right()
    {
        var admin = await api.SignUpAdmin();
        var (author, commenter, replier, reporter) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (chapter, paragraphs) = await api.AddChapter(await api.AddNovel(author), "<p>الأولى</p>", "<p>الثانية</p>");
        var chapterUrl = $"/api/comment/chapter/{chapter.Id}";
        var paragraphUrl = $"/api/comment/paragraph/{paragraphs[0].Id}";
        var reported = await api.Comment(commenter, chapterUrl, "تعليق مخالف");
        var reply = await api.Comment(replier, chapterUrl, "رد", parentId: reported);
        var kept = await api.Comment(commenter, chapterUrl, "تعليق سليم");
        var onParagraph = await api.Comment(commenter, paragraphUrl, "على الفقرة");
        var replyOnParagraph = await api.Comment(replier, paragraphUrl, "رد على الفقرة", parentId: onParagraph);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/comment/{reply}/like", commenter)).StatusCode);
        // The notifications these made (in the background): the replies and the like, and the author's for each comment.
        await api.WaitForNotificationsFrom(commenter, replier, count: 2);
        await api.WaitForNotificationsFrom(replier, commenter);
        await api.WaitForNotificationsFrom(author, commenter, count: 3);
        var report = await api.Reported(reporter, "Comment", reported);
        var paragraphReport = await api.Reported(reporter, "Comment", onParagraph);

        var removed = await (await api.Resolve(admin, report, "RemoveContent")).OkJson();
        await api.Resolve(admin, paragraphReport, "RemoveContent");

        Assert.True(removed.GetProperty("contentRemoved").GetBoolean());
        Assert.Equal(1, removed.GetProperty("resolvedReports").GetInt32());
        await using var db = api.Db();
        var gone = new[] { reported, reply, onParagraph, replyOnParagraph };
        Assert.False(await db.Comments.IgnoreQueryFilters().AnyAsync(c => gone.Contains(c.Id)));
        Assert.False(await db.CommentLikes.AnyAsync(l => gone.Contains(l.CommentId)));
        Assert.False(await db.Notifications.AnyAsync(n => n.RelatedEntityId != null && gone.Contains(n.RelatedEntityId.Value)));
        Assert.True(await db.Comments.AnyAsync(c => c.Id == kept));

        // Every counter matches a recount of what is left.
        var savedChapter = await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == chapter.Id);
        Assert.Equal(1, savedChapter.CommentsCount);
        Assert.Equal(1, savedChapter.TotalCommentsCount);
        Assert.Equal(0, (await db.ChapterParagraphs.AsNoTracking().SingleAsync(p => p.Id == paragraphs[0].Id)).CommentsCount);
        foreach (var user in new[] { commenter, replier })
        {
            var counted = await db.Users.Where(u => u.Id == user.Id).Select(u => u.CommentsCount).SingleAsync();
            Assert.Equal(await db.Comments.CountAsync(c => c.UserId == user.Id), counted);
        }
        Assert.Equal(1, (await (await api.Get(chapterUrl)).OkJson()).GetProperty("totalItemsCount").GetInt32());
        Assert.Equal(ReportStatus.Resolved, (await Stored(report)).Status);
        Assert.Equal(ReportAction.RemoveContent, (await Stored(report)).Resolution);
    }

    [Fact]
    public async Task Removing_a_post_comment_lowers_the_posts_count()
    {
        var admin = await api.SignUpAdmin();
        var (author, commenter, reporter) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);
        var url = $"/api/comment/post/{post}";
        var reported = await api.Comment(commenter, url, "مخالف");
        await api.Comment(commenter, url, "سليم");
        await api.Comment(author, url, "رد", parentId: reported);

        await api.Resolve(admin, await api.Reported(reporter, "Comment", reported), "RemoveContent");

        await using var db = api.Db();
        Assert.Equal(1, (await db.Posts.AsNoTracking().SingleAsync(p => p.Id == post)).CommentsCount);
        Assert.Equal(1, await db.Users.Where(u => u.Id == commenter.Id).Select(u => u.CommentsCount).SingleAsync());
        Assert.Equal(0, await db.Users.Where(u => u.Id == author.Id).Select(u => u.CommentsCount).SingleAsync());
    }

    [Fact]
    public async Task Removing_a_review_deletes_its_likes_and_recomputes_the_novels_stats()
    {
        var admin = await api.SignUpAdmin();
        var (author, harsh, fair, reporter) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var reported = await api.Review(harsh, novel.Id, score: 1, content: "تقييم مسيء للكاتب");
        await api.Review(fair, novel.Id, score: 5);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/{novel.Id}/reviews/{reported}/like", reporter)).StatusCode);

        var result = await (await api.Resolve(admin, await api.Reported(reporter, "Review", reported), "RemoveContent")).OkJson();

        Assert.True(result.GetProperty("contentRemoved").GetBoolean());
        await using var db = api.Db();
        Assert.False(await db.Reviews.AnyAsync(r => r.Id == reported));
        Assert.False(await db.ReviewLikes.AnyAsync(l => l.ReviewId == reported));
        var stats = await db.Novels.AsNoTracking().SingleAsync(n => n.Id == novel.Id);
        Assert.Equal(1, stats.ReviewCount);
        Assert.Equal(5m, stats.TotalAverageScore);
        Assert.Equal(0, await db.Users.Where(u => u.Id == harsh.Id).Select(u => u.ReviewsCount).SingleAsync());
    }

    [Fact]
    public async Task Removing_a_post_a_novel_or_a_reading_list_deletes_it_as_its_author_would()
    {
        var admin = await api.SignUpAdmin();
        var (author, reporter) = (await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);
        var novel = await api.AddNovel(author);
        var list = await api.ReadingList(author);

        foreach (var (type, id) in new[] { ("Post", post), ("Novel", novel.Id), ("ReadingList", list) })
        {
            var result = await (await api.Resolve(admin, await api.Reported(reporter, type, id), "RemoveContent")).OkJson();
            Assert.True(result.GetProperty("contentRemoved").GetBoolean(), type);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/posts/{post}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/novel/by-id/{novel.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/readinglist/{list}", reporter)).StatusCode);
        await using var db = api.Db();
        Assert.True((await db.Posts.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == post)).IsDeleted);
        var deletedNovel = await db.Novels.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.Id == novel.Id);
        Assert.True(deletedNovel.IsDeleted);
        Assert.False(deletedNovel.IsEligibleForRanking);
    }

    [Fact]
    public async Task An_authors_own_novel_delete_is_the_same_soft_delete()
    {
        var (author, stranger) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);

        Assert.Equal(HttpStatusCode.Forbidden, (await api.Send(HttpMethod.Delete, $"/api/myworks/{novel.Id}/delete", stranger)).StatusCode);
        var deleted = await (await api.Send(HttpMethod.Delete, $"/api/myworks/{novel.Id}/delete", author)).OkJson();

        Assert.True(deleted.GetProperty("success").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/novel/by-id/{novel.Id}")).StatusCode);
        await using var db = api.Db();
        var stored = await db.Novels.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.Id == novel.Id);
        Assert.True(stored.IsDeleted);
        Assert.False(stored.IsEligibleForRanking);
        Assert.Equal(novel.Title, stored.Title);
    }

    [Fact]
    public async Task Content_already_gone_just_gets_its_reports_closed()
    {
        var admin = await api.SignUpAdmin();
        var (author, first, second) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);
        var report = await api.Reported(first, "Post", post);
        var afterwards = await api.Reported(second, "Post", post);
        await api.Resolve(admin, report, "RemoveContent");

        // The second report was closed by the first action; acting on it again removes nothing and closes nothing.
        var again = await (await api.Resolve(admin, afterwards, "RemoveContent")).OkJson();

        Assert.False(again.GetProperty("contentRemoved").GetBoolean());
        Assert.Equal(0, again.GetProperty("resolvedReports").GetInt32());
        Assert.Equal(ReportStatus.Resolved, (await Stored(afterwards)).Status);
    }

    [Fact]
    public async Task A_user_is_suspended_not_removed()
    {
        var admin = await api.SignUpAdmin();
        var (user, reporter) = (await api.SignUp(), await api.SignUp());
        var report = await api.Reported(reporter, "User", user.Id);

        var error = await (await api.Resolve(admin, report, "RemoveContent")).Error(HttpStatusCode.BadRequest);

        Assert.Equal("InvalidAction", error.GetProperty("code").GetString());
        Assert.Equal(ReportStatus.Open, (await Stored(report)).Status);
    }

    [Theory]
    [InlineData("Delete", null)]
    [InlineData(null, null)]
    [InlineData("SuspendUser", 0)]
    [InlineData("SuspendUser", 3651)]
    public async Task An_unknown_action_or_a_suspension_out_of_range_is_a_400(string? action, int? days)
    {
        var admin = await api.SignUpAdmin();
        var (user, reporter) = (await api.SignUp(), await api.SignUp());
        var report = await api.Reported(reporter, "User", user.Id);

        var response = await api.Send(HttpMethod.Patch, $"/api/admin/reports/{report}", admin,
            JsonContent.Create(new { action, suspensionDays = days }));

        await response.Error(HttpStatusCode.BadRequest);
        Assert.Equal(ReportStatus.Open, (await Stored(report)).Status);
    }

    [Fact]
    public async Task An_unknown_report_is_a_404()
    {
        var admin = await api.SignUpAdmin();

        var error = await (await api.Resolve(admin, Guid.NewGuid(), "Dismiss")).Error(HttpStatusCode.NotFound);

        Assert.Equal("ReportNotFound", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Suspending_ends_the_sessions_refuses_sign_in_until_lifted_and_closes_the_reports()
    {
        var admin = await api.SignUpAdmin();
        var (author, reporter) = (await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);
        var report = await api.Reported(reporter, "Post", post);
        // A session from a minute ago: the cut-off has whole-second precision, so a token from the suspending second
        // itself would pass it again once the suspension is lifted (as in TokenRevocationHttpTests).
        var session = author with
        {
            Token = TokenFactory.Write(author.Id, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddDays(59),
                api.Services.GetRequiredService<IConfiguration>()["Jwt:Key"]!)
        };
        Assert.Equal(HttpStatusCode.OK, (await api.Get("/api/User/my-profile", session)).StatusCode);

        var result = await (await api.Resolve(admin, report, "SuspendUser", suspensionDays: 7)).OkJson();

        Assert.Equal(author.Id, result.GetProperty("suspendedUserId").GetString());
        Assert.False(result.GetProperty("suspendedPermanently").GetBoolean());
        var until = result.GetProperty("suspendedUntil").GetDateTime();
        Assert.InRange(until, DateTime.UtcNow.AddDays(7).AddMinutes(-5), DateTime.UtcNow.AddDays(7).AddMinutes(5));
        Assert.Equal(1, result.GetProperty("resolvedReports").GetInt32());
        Assert.Equal(ReportAction.SuspendUser, (await Stored(report)).Resolution);
        // The content stays: suspending and removing are separate actions.
        Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/posts/{post}")).StatusCode);

        // Its sessions are over (even the newest), and signing in (password or Google) is refused with a code the apps
        // branch on.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/User/my-profile", session)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/User/my-profile", author)).StatusCode);
        var refused = await (await api.Login(author.UserName)).Error(HttpStatusCode.Forbidden);
        Assert.Equal("AccountSuspended", refused.GetProperty("code").GetString());
        Assert.Contains("تم إيقاف حسابك مؤقتاً حتى", refused.GetProperty("message").GetString());
        Assert.False(refused.GetProperty("permanent").GetBoolean());
        Assert.Equal(until, refused.GetProperty("suspendedUntil").GetDateTime(), TimeSpan.FromSeconds(1));
        var google = await api.Client().PostAsJsonAsync("/api/identity/google-login",
            new { idToken = api.GoogleTokens.Issue($"{author.UserName}@example.test") });
        Assert.Equal("AccountSuspended", (await google.Error(HttpStatusCode.Forbidden)).GetProperty("code").GetString());

        // The admin list shows the author as suspended.
        var owner = Item(await api.AdminReports(admin, "Resolved"), report).GetProperty("target").GetProperty("owner");
        Assert.True(owner.GetProperty("isSuspended").GetBoolean());
        Assert.Equal(until, owner.GetProperty("suspendedUntil").GetDateTime(), TimeSpan.FromSeconds(1));

        var lifted = await (await api.Send(HttpMethod.Delete, $"/api/admin/users/{author.Id}/suspension", admin)).OkJson();
        Assert.False(lifted.GetProperty("isSuspended").GetBoolean());
        var token = await api.SignIn(author.UserName);
        Assert.Equal(HttpStatusCode.OK, (await api.Get("/api/User/my-profile", author with { Token = token })).StatusCode);
        // The session the suspension ended stays ended.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/User/my-profile", session)).StatusCode);
    }

    [Fact]
    public async Task Without_a_number_of_days_a_suspension_is_permanent_and_is_never_shortened()
    {
        var admin = await api.SignUpAdmin();
        var (user, first, second) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var post = await api.Post(user);

        var permanent = await (await api.Resolve(admin, await api.Reported(first, "User", user.Id), "SuspendUser")).OkJson();
        var later = await (await api.Resolve(admin, await api.Reported(second, "Post", post), "SuspendUser", suspensionDays: 1)).OkJson();

        foreach (var result in new[] { permanent, later })
        {
            Assert.True(result.GetProperty("suspendedPermanently").GetBoolean());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("suspendedUntil").ValueKind);
        }
        var refused = await (await api.Login(user.UserName)).Error(HttpStatusCode.Forbidden);
        Assert.True(refused.GetProperty("permanent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, refused.GetProperty("suspendedUntil").ValueKind);
        Assert.Contains("نهائياً", refused.GetProperty("message").GetString());
        await using var db = api.Db();
        Assert.Equal(Suspension.Permanent, await db.Users.Where(u => u.Id == user.Id).Select(u => u.SuspendedUntil).SingleAsync());
    }

    [Fact]
    public async Task An_admin_cannot_suspend_their_own_account_and_lifting_an_unknown_user_is_a_404()
    {
        var admin = await api.SignUpAdmin();
        var reporter = await api.SignUp();
        var report = await api.Reported(reporter, "User", admin.Id);

        var error = await (await api.Resolve(admin, report, "SuspendUser")).Error(HttpStatusCode.BadRequest);
        Assert.Equal("CannotSuspendSelf", error.GetProperty("code").GetString());

        var unknown = await (await api.Send(HttpMethod.Delete, $"/api/admin/users/{Guid.NewGuid()}/suspension", admin)).Error(HttpStatusCode.NotFound);
        Assert.Equal("UserNotFound", unknown.GetProperty("code").GetString());
    }
}
