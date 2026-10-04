using System.Data.Common;
using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sareed_novels_backend.Tests.Unit;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #66, <see cref="RecountStoredCounters"/>, on a history made through the API the way members make it: comments on
/// posts, chapters and paragraphs with replies, deleted by their authors, removed by a moderator or with the paragraph
/// an edit drops or the chapter its author deletes; likes and unlikes; reviews written, edited, deleted and removed,
/// with their likes; libraries, reading lists and their followers; a deleted post, novel and account. After it the
/// recount finds nothing to change: it computes what the live code maintains. Every counter corrupted in SQL comes back
/// to what the live code left, the diagnostic the owner runs before the deploy reports exactly the rows the migration
/// fixes (and nothing after), and running the recount again changes nothing; a comment posted while it runs waits for
/// it. Its own database: the recount runs over every row.
/// </summary>
public class RecountStoredCountersHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private const string Password = "Correct-horse-1";

    private sealed record Value(string Counter, string Id, decimal Stored);

    private sealed record SummaryRow(int Step, string Counter, int RowsChecked, int RowsToChange, int StoredTooHigh,
        int StoredTooLow, decimal TotalAbsoluteDrift);

    private sealed record ChangedRow(string Counter, string Id, decimal Stored, decimal Recounted);

    private sealed record Diagnosis(List<SummaryRow> Summary, List<ChangedRow> Rows, List<string> Places);

    private sealed record IdValue(string Id, decimal Value);

    [Fact]
    public async Task The_recount_finds_a_real_history_right_and_puts_back_every_counter_corrupted_in_sql()
    {
        var reportedPost = await MakeHistory();

        // The live code kept every counter at what the recount gives, and comments are each in one place.
        var diagnosis = await Diagnose();
        Assert.Equal(RecountStoredCounters.Counters.Select(c => c.Name), diagnosis.Summary.Select(s => s.Counter));
        Assert.All(diagnosis.Summary, s => Assert.Equal(0, s.RowsToChange));
        Assert.Empty(diagnosis.Rows);
        Assert.Equal(["chapter", "paragraph", "post"], diagnosis.Places);

        var snapshot = await Snapshot();
        foreach (var counter in RecountStoredCounters.Counters)
        {
            Assert.Contains(snapshot, v => v.Counter == counter.Name && v.Stored != 0);
        }

        // Corrupted: of each counter's rows, two in three, too high or too low; the reported post says 0.
        var corrupted = Corrupt(snapshot, reportedPost);
        await Store(corrupted);
        var expected = corrupted
            .Join(snapshot, c => (c.Counter, c.Id), s => (s.Counter, s.Id), (c, s) => new ChangedRow(c.Counter, c.Id, c.Stored, s.Stored))
            .Where(r => r.Stored != r.Recounted)
            .ToList();
        Assert.Contains(expected, r => r == new ChangedRow("Posts.CommentsCount", Id(reportedPost), 0, 1));
        foreach (var counter in RecountStoredCounters.Counters)
        {
            Assert.Contains(expected, r => r.Counter == counter.Name);
        }

        // The diagnostic reports exactly those rows, with the values the migration will store.
        diagnosis = await Diagnose();
        Assert.Equal(Sorted(expected), Sorted(diagnosis.Rows));
        foreach (var (summary, counter) in diagnosis.Summary.Zip(RecountStoredCounters.Counters))
        {
            var changed = expected.Where(r => r.Counter == counter.Name).ToList();
            Assert.Equal(
                new SummaryRow(summary.Step, counter.Name, snapshot.Count(v => v.Counter == counter.Name), changed.Count,
                    changed.Count(r => r.Stored > r.Recounted), changed.Count(r => r.Stored < r.Recounted),
                    changed.Sum(r => Math.Abs(r.Stored - r.Recounted))),
                summary);
        }

        // The migration: Down changes nothing; Up puts back every counter.
        await using (var db = api.Db())
        {
            var migrator = db.GetService<IMigrator>();
            var migrations = db.Database.GetMigrations().ToList();
            await migrator.MigrateAsync(migrations[migrations.IndexOf(RecountId) - 1]);
            Assert.Equal(Sorted(Corrupted(snapshot, corrupted)), Sorted(await Snapshot()));
            await migrator.MigrateAsync();
            Assert.Contains(RecountId, await db.Database.GetAppliedMigrationsAsync());
        }
        Assert.Equal(Sorted(snapshot), Sorted(await Snapshot()));

        // Again: nothing left to change, and the diagnostic agrees.
        Assert.Equal(0, await Recount());
        diagnosis = await Diagnose();
        Assert.All(diagnosis.Summary, s => Assert.Equal((0, 0m), (s.RowsToChange, s.TotalAbsoluteDrift)));
        Assert.Empty(diagnosis.Rows);
        Assert.Equal(Sorted(snapshot), Sorted(await Snapshot()));

        // Only the rows that differ are written.
        await Store(corrupted);
        Assert.Equal(expected.Count, await Recount());
        Assert.Equal(Sorted(snapshot), Sorted(await Snapshot()));
    }

    [Fact]
    public async Task A_post_with_one_comment_and_a_reply_stored_as_zero_says_one_after_the_recount()
    {
        // As post 17f1d72d in production: one comment (with a reply) on the post, its count 0.
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var post = await api.Post(author, "بسم الله");
        var comment = await api.Comment(reader, $"/api/comment/post/{post}", "سبحان الله والله اكبر");
        await api.Comment(author, $"/api/comment/post/{post}", "رد", parentId: comment);
        await Store([new Value("Posts.CommentsCount", Id(post), 0)]);

        Assert.Equal(0, await CommentsCount(post));
        Assert.Equal(1, (await (await api.Get($"/api/comment/post/{post}")).OkJson()).GetProperty("totalItemsCount").GetInt32());
        Assert.Contains(new ChangedRow("Posts.CommentsCount", Id(post), 0, 1), (await Diagnose()).Rows);

        await Recount();

        Assert.Equal(1, await CommentsCount(post));
        Assert.DoesNotContain((await Diagnose()).Rows, r => r.Id == Id(post));
    }

    [Fact]
    public async Task A_comment_posted_while_the_recount_runs_waits_for_it_and_is_counted()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);

        // The migration's batch, run but not committed yet (as while the deploy applies it): it has read every post's
        // comments, and holds them.
        await using var connection = new SqlConnection(api.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using (var command = new SqlCommand(RecountStoredCounters.Recount, connection, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        // A comment posted meanwhile waits for the commit, so its +1 lands on the recounted value.
        var posting = api.Comment(reader, $"/api/comment/post/{post}");
        Assert.NotSame(posting, await Task.WhenAny(posting, Task.Delay(TimeSpan.FromSeconds(2))));
        await transaction.CommitAsync();
        await posting;

        Assert.Equal(1, await CommentsCount(post));
        Assert.Empty((await Diagnose()).Rows);
    }

    private async Task<int> CommentsCount(Guid post) =>
        (await (await api.Get($"/api/posts/{post}")).OkJson()).GetProperty("commentsCount").GetInt32();

    private static readonly string RecountId = typeof(RecountStoredCounters).GetCustomAttribute<MigrationAttribute>()!.Id;

    /// <summary>
    /// Everything that moves a counter, through the API. Returns the post shaped like production's 17f1d72d: one
    /// comment, with a reply.
    /// </summary>
    private async Task<Guid> MakeHistory()
    {
        var admin = await api.SignUpAdmin();
        var (author, r1, r2, r3) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (leaving, reporter) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var other = await api.AddNovel(author);
        var single = await api.AddNovel(author);
        await api.AddNovel(author); // nothing on it: every counter 0

        // Chapters as the author's editor saves them, split into paragraphs; one a draft.
        var chapter = await Chapter(author, novel, "<p>الأولى</p><p>الثانية</p><p>الثالثة</p><p>الرابعة</p>");
        var doomedChapter = await Chapter(author, novel, "<p>فقرة</p><p>فقرة أخرى</p>");
        await Chapter(author, novel, "<p>مسودة</p>", ChapterStatuses.Draft);
        var otherChapter = await Chapter(author, other, "<p>نص</p><p>نص آخر</p>");
        var onlyChapter = await Chapter(author, single, "<p>الفصل الوحيد</p>");
        var paragraphs = await Paragraphs(chapter);
        var doomedParagraphs = await Paragraphs(doomedChapter);

        // Posts: the reported case, and one whose comment its author deletes, deleted itself in the end.
        var reportedPost = await api.Post(author, "بسم الله");
        var first = await api.Comment(r1, PostComments(reportedPost), "سبحان الله والله اكبر");
        await api.Comment(author, PostComments(reportedPost), "رد", parentId: first);
        var post = await api.Post(r1);
        var deletedOnPost = await api.Comment(r2, PostComments(post));
        var keptOnPost = await api.Comment(r3, PostComments(post));
        await api.Comment(r1, PostComments(post), "رد", parentId: keptOnPost);

        // On the chapter: a comment its author deletes, whose reply stays; a reply its author deletes.
        var deletedThread = await api.Comment(r1, ChapterComments(chapter));
        await api.Comment(r2, ChapterComments(chapter), "رد", parentId: deletedThread);
        var chapterComment = await api.Comment(r2, ChapterComments(chapter));
        var deletedReply = await api.Comment(r3, ChapterComments(chapter), "رد", parentId: chapterComment);

        // On its paragraphs: a reply a moderator removes, a comment a moderator removes with its reply, a paragraph
        // the edit removes with its comments, and the comment of a member who deletes their account.
        var onFirst = await api.Comment(r3, ParagraphComments(paragraphs[0]));
        var removedReply = await api.Comment(r1, ParagraphComments(paragraphs[0]), "رد", parentId: onFirst);
        var removedThread = await api.Comment(r1, ParagraphComments(paragraphs[1]));
        await api.Comment(r2, ParagraphComments(paragraphs[1]), "رد", parentId: removedThread);
        var onRemovedParagraph = await api.Comment(r2, ParagraphComments(paragraphs[2]));
        await api.Comment(r3, ParagraphComments(paragraphs[2]), "رد", parentId: onRemovedParagraph);
        await api.Comment(leaving, ParagraphComments(paragraphs[3]));

        // On the chapter its author deletes, and its paragraph.
        var onDoomed = await api.Comment(r1, ChapterComments(doomedChapter));
        await api.Comment(r3, ChapterComments(doomedChapter), "رد", parentId: onDoomed);
        await api.Comment(r2, ParagraphComments(doomedParagraphs[0]));

        // Likes and unlikes, some of things deleted or removed later.
        foreach (var liker in new[] { r1, r2, r3, leaving })
        {
            await Ok(HttpMethod.Post, $"/api/posts/{reportedPost}/like", liker);
        }
        await Ok(HttpMethod.Delete, $"/api/posts/{reportedPost}/unlike", r2);
        await Ok(HttpMethod.Post, $"/api/posts/{post}/like", r2);
        foreach (var liker in new[] { r2, r3, leaving })
        {
            await Ok(HttpMethod.Post, $"/api/comment/{first}/like", liker);
        }
        await Ok(HttpMethod.Delete, $"/api/comment/{first}/unlike", r3);
        foreach (var (comment, liker) in new[]
                 {
                     (deletedThread, r3), (chapterComment, r1), (keptOnPost, r2), (removedReply, r2), (removedThread, r3),
                     (onRemovedParagraph, r1), (onDoomed, r2)
                 })
        {
            await Ok(HttpMethod.Post, $"/api/comment/{comment}/like", liker);
        }

        // Reviews, with scores whose averages round at the third decimal: as RefreshNovelReviewStats stores them.
        var r1Review = await Review(r1, novel, 4.01m, 3.33m, 5m, 2.5m);
        var r2Review = await Review(r2, novel, 4.02m, 3.34m, 4.5m, 1m);
        var r3Review = await Review(r3, novel, 1m, 2m, 3m, 4m);
        await Review(leaving, novel, 5m, 5m, 5m, 5m);
        await Ok(HttpMethod.Patch, $"/api/{novel.Id}/reviews/{r2Review}", r2, JsonContent.Create(new
        {
            writingQualityScore = 4.03m, updatingStabilityScore = 3.33m, characterDevelopmentScore = 4.75m, worldBuildingScore = 1.25m
        }));
        var otherReview = await Review(r1, other, 4.01m, 4.01m, 4.01m, 4.03m);
        var removedReview = await Review(r2, other, 2m, 2m, 2m, 2m);
        await Review(r2, single, 3.01m, 2m, 1m, 1m);
        await Review(r3, single, 3.02m, 2.01m, 1m, 1m);
        foreach (var liker in new[] { r2, r3, leaving })
        {
            await Ok(HttpMethod.Post, $"/api/{novel.Id}/reviews/{r1Review}/like", liker);
        }
        await Ok(HttpMethod.Delete, $"/api/{novel.Id}/reviews/{r1Review}/unlike", r2);
        await Ok(HttpMethod.Post, $"/api/{novel.Id}/reviews/{r3Review}/like", r1);
        await Ok(HttpMethod.Post, $"/api/{other.Id}/reviews/{otherReview}/like", r3);
        await Ok(HttpMethod.Post, $"/api/{other.Id}/reviews/{removedReview}/like", r1);

        // Libraries: read, removed, moved off a deleted chapter, gone with a novel's only chapter.
        foreach (var (reader, readChapter) in new[]
                 {
                     (r1, chapter), (r1, otherChapter), (r2, doomedChapter), (r3, otherChapter), (r3, onlyChapter), (leaving, chapter)
                 })
        {
            await Ok(HttpMethod.Post, $"/api/library/track-progress/{readChapter}", reader);
        }
        await Ok(HttpMethod.Delete, $"/api/library/novel/{other.Id}", r1);

        // Reading lists: one with novels added and removed, followed and unfollowed; one of the member who leaves; one
        // a moderator removes; one its owner deletes.
        var list = await ReadingList(r1, novel);
        await Ok(HttpMethod.Post, $"/api/readinglist/{list}/novels/{other.Id}", r1);
        await Ok(HttpMethod.Post, $"/api/readinglist/{list}/novels/{single.Id}", r1);
        await Ok(HttpMethod.Delete, $"/api/readinglist/{list}/novels/{single.Id}", r1);
        foreach (var follower in new[] { r2, r3, leaving })
        {
            await Ok(HttpMethod.Post, $"/api/readinglist/{list}/follow", follower);
        }
        await Ok(HttpMethod.Delete, $"/api/readinglist/{list}/unfollow", r3);
        await Ok(HttpMethod.Post, $"/api/readinglist/{await ReadingList(leaving, novel)}/follow", r1);
        var removedList = await ReadingList(r2, novel);
        await Ok(HttpMethod.Post, $"/api/readinglist/{removedList}/follow", r1);
        var deletedList = await ReadingList(r3, single);
        await Ok(HttpMethod.Post, $"/api/readinglist/{deletedList}/follow", r2);
        await Ok(HttpMethod.Delete, $"/api/readinglist/{deletedList}", r3);

        // Deletions by their authors.
        await Ok(HttpMethod.Delete, $"/api/comment/{deletedOnPost}", r2);
        await Ok(HttpMethod.Delete, $"/api/comment/{deletedThread}", r1);
        await Ok(HttpMethod.Delete, $"/api/comment/{deletedReply}", r3);
        await Ok(HttpMethod.Delete, $"/api/{novel.Id}", r3);
        await Ok(HttpMethod.Delete, $"/api/posts/{post}", r1);

        // A moderator removes a reply, a comment with its reply, a review and a reading list.
        foreach (var (type, id) in new[] { ("Comment", removedReply), ("Comment", removedThread), ("Review", removedReview), ("ReadingList", removedList) })
        {
            await Ok(await api.Resolve(admin, await api.Reported(reporter, type, id), "RemoveContent"));
        }

        // The author edits the chapter without its third paragraph, deletes a chapter, and the only chapter of a novel
        // (whose reader's progress goes with it), then deletes another novel.
        await Ok(HttpMethod.Patch, $"/api/novel/{novel.Id}/chapter/{chapter}", author, JsonContent.Create(new
        {
            status = ChapterStatuses.Published, title = "الفصل الأول", content = "<p>الأولى</p><p>الثانية</p><p>الرابعة</p>"
        }));
        await Ok(HttpMethod.Delete, $"/api/novel/{novel.Id}/chapter/{doomedChapter}", author);
        await Ok(HttpMethod.Delete, $"/api/novel/{single.Id}/chapter/{onlyChapter}", author);
        await Ok(HttpMethod.Delete, $"/api/myworks/{other.Id}/delete", author);

        // A member deletes their account: their likes, follows and library go; their comment and review stay.
        await Ok(HttpMethod.Delete, "/api/User/me", leaving, JsonContent.Create(new { password = Password }));

        await AssertWhatTheRulesSay();
        return reportedPost;

        // A few of the values the history leaves, as each counter's rule says.
        async Task AssertWhatTheRulesSay()
        {
            var values = (await Snapshot()).ToDictionary(v => (v.Counter, v.Id), v => v.Stored);
            decimal Of(string counter, object id) => values[(counter, Id(id))];

            // A reply counts for no post; a deleted post keeps its comments and likes.
            Assert.Equal((1m, 2m), (Of("Posts.CommentsCount", reportedPost), Of("Posts.LikesCount", reportedPost)));
            Assert.Equal((1m, 1m), (Of("Posts.CommentsCount", post), Of("Posts.LikesCount", post)));

            // The chapter: its comment left (the other deleted by its author), and one each on the first and the last
            // paragraph (the second's removed, the third's paragraph gone); three paragraphs now.
            Assert.Equal((1m, 3m, 3m), (Of("Chapters.CommentsCount", chapter), Of("Chapters.TotalCommentsCount", chapter),
                Of("Chapters.ParagraphsCount", chapter)));
            Assert.Equal((1m, 0m, 1m), (Of("ChapterParagraphs.CommentsCount", paragraphs[0]),
                Of("ChapterParagraphs.CommentsCount", paragraphs[1]), Of("ChapterParagraphs.CommentsCount", paragraphs[3])));

            // A member's comments: replies too, one in a thread its author deleted among them; the deleted and the
            // removed ones not. The account deleted keeps its comment and review.
            Assert.Equal(2m, Of("AspNetUsers.CommentsCount", r2.Id));
            Assert.Equal((1m, 1m), (Of("AspNetUsers.CommentsCount", leaving.Id), Of("AspNetUsers.ReviewsCount", leaving.Id)));

            // Likes left after the unlikes and the account deletion.
            Assert.Equal(1m, Of("Comments.LikesCount", first));
            Assert.Equal(1m, Of("Comments.LikesCount", deletedThread));
            Assert.Equal(1m, Of("Reviews.LikeCount", r1Review));

            // Review stats as stored: the averages round half away from zero, the total from the unrounded averages.
            Assert.Equal(3m, Of("Novels.ReviewCount", novel.Id));
            Assert.Equal((3.02m, 2.01m, 1m, 1m, 1.76m), (Of("Novels.AverageWritingQualityScore", single.Id),
                Of("Novels.AverageUpdatingStabilityScore", single.Id), Of("Novels.AverageCharacterDevelopmentScore", single.Id),
                Of("Novels.AverageWorldBuildingScore", single.Id), Of("Novels.TotalAverageScore", single.Id)));
            Assert.Equal((1m, 4.02m), (Of("Novels.ReviewCount", other.Id), Of("Novels.TotalAverageScore", other.Id)));

            // Libraries: a deleted novel still counts; the account deleted has none.
            Assert.Equal((1m, 1m, 1m, 0m), (Of("AspNetUsers.LibraryNovelsCount", r1.Id), Of("AspNetUsers.LibraryNovelsCount", r2.Id),
                Of("AspNetUsers.LibraryNovelsCount", r3.Id), Of("AspNetUsers.LibraryNovelsCount", leaving.Id)));

            // The list: two novels (one deleted since), one follower left.
            Assert.Equal((2m, 1m), (Of("ReadingLists.NovelsCount", list), Of("ReadingLists.FollowersCount", list)));

            // Published chapters: the draft doesn't count, the deleted chapters are gone.
            Assert.Equal((1m, 0m), (Of("Novels.ChapterCount", novel.Id), Of("Novels.ChapterCount", single.Id)));
        }
    }

    private static string PostComments(Guid post) => $"/api/comment/post/{post}";

    private static string ChapterComments(Guid chapter) => $"/api/comment/chapter/{chapter}";

    private static string ParagraphComments(Guid paragraph) => $"/api/comment/paragraph/{paragraph}";

    /// <summary>An id as the scripts print it: a uniqueidentifier in capitals, a member's (text) as stored.</summary>
    private static string Id(object id) => id is Guid guid ? guid.ToString().ToUpperInvariant() : (string)id;

    private async Task<Guid> Chapter(ApiUser author, Novel novel, string content, string status = ChapterStatuses.Published)
    {
        var response = await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author,
            JsonContent.Create(new { status, title = "فصل " + Seed.Marker(), content }));
        return (await response.OkJson()).GetProperty("id").GetGuid();
    }

    private async Task<List<Guid>> Paragraphs(Guid chapter)
    {
        await using var db = api.Db();
        return await db.ChapterParagraphs.Where(p => p.ChapterId == chapter).OrderBy(p => p.OrderIndex).Select(p => p.Id).ToListAsync();
    }

    private async Task<Guid> Review(ApiUser reviewer, Novel novel, decimal writing, decimal stability, decimal characters, decimal world)
    {
        var response = await api.Send(HttpMethod.Post, $"/api/{novel.Id}", reviewer, JsonContent.Create(new
        {
            writingQualityScore = writing, updatingStabilityScore = stability, characterDevelopmentScore = characters,
            worldBuildingScore = world, isSpoiler = false, content = "رواية جميلة جداً"
        }));
        return (await response.OkJson()).GetProperty("review").GetProperty("id").GetGuid();
    }

    /// <summary>A public reading list of <paramref name="owner"/>'s, made with <paramref name="novel"/> on it.</summary>
    private async Task<Guid> ReadingList(ApiUser owner, Novel novel)
    {
        var response = await api.Send(HttpMethod.Post, "/api/readinglist", owner,
            ReaderApi.Form(("Name", "قائمة " + Seed.Marker()), ("IsPublic", "true"), ("NovelId", novel.Id.ToString())));
        return (await response.OkJson()).GetProperty("readingList").GetProperty("id").GetGuid();
    }

    private async Task Ok(HttpMethod method, string url, ApiUser user, HttpContent? content = null) =>
        await Ok(await api.Send(method, url, user, content));

    private static async Task Ok(HttpResponseMessage response) =>
        Assert.True(response.IsSuccessStatusCode,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");

    /// <summary>Every stored counter of every row, as the diagnostic reads them.</summary>
    private async Task<List<Value>> Snapshot()
    {
        await using var db = api.Db();
        var values = new List<Value>();
        foreach (var counter in RecountStoredCounters.Counters)
        {
            var rows = await db.Database.SqlQueryRaw<IdValue>(SelectSql(counter)).ToListAsync();
            values.AddRange(rows.Select(r => new Value(counter.Name, r.Id, r.Value)));
        }
        return values;
    }

    private static string SelectSql(RecountStoredCounters.Counter counter) =>
        $"SELECT CONVERT(nvarchar(450), Id) AS Id, CAST({counter.Column} AS decimal(19, 2)) AS Value FROM {counter.Table}";

    /// <summary>
    /// Wrong values for two in three of each counter's rows (the first of them among them): too high, or too low (0)
    /// where the value isn't 0 already; the reported post's comment count is 0.
    /// </summary>
    private static List<Value> Corrupt(List<Value> snapshot, Guid reportedPost) =>
        snapshot
            .GroupBy(v => v.Counter)
            .SelectMany(rows => rows.OrderBy(v => v.Id, StringComparer.Ordinal).Select((v, i) =>
                v.Counter == "Posts.CommentsCount" && v.Id == Id(reportedPost) ? v with { Stored = 0 }
                : (i % 3) switch
                {
                    0 => v with { Stored = v.Stored + 1 + i % 4 },
                    1 => v with { Stored = v.Stored == 0 ? 2 : 0 },
                    _ => null
                }))
            .OfType<Value>()
            .ToList();

    private static List<Value> Corrupted(List<Value> snapshot, List<Value> corrupted)
    {
        var wrong = corrupted.ToDictionary(v => (v.Counter, v.Id), v => v.Stored);
        return snapshot.Select(v => wrong.TryGetValue((v.Counter, v.Id), out var stored) ? v with { Stored = stored } : v).ToList();
    }

    private async Task Store(IEnumerable<Value> values)
    {
        await using var db = api.Db();
        foreach (var value in values)
        {
            var counter = RecountStoredCounters.Counters.Single(c => c.Name == value.Counter);
            var sql = UpdateSql(counter);
            Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(sql, new SqlParameter("@value", value.Stored), new SqlParameter("@id", value.Id)));
        }
    }

    private static string UpdateSql(RecountStoredCounters.Counter counter) =>
        $"UPDATE {counter.Table} SET {counter.Column} = @value WHERE CONVERT(nvarchar(450), Id) = @id";

    /// <summary>The migration's SQL, run again; the rows it changed.</summary>
    private async Task<int> Recount()
    {
        await using var db = api.Db();
        return await db.Database.ExecuteSqlRawAsync(RecountStoredCounters.Recount);
    }

    /// <summary>The checked-in diagnostic, statement by statement, as the owner runs it.</summary>
    private async Task<Diagnosis> Diagnose()
    {
        var statements = RecountStoredCountersDiagnosticTests.Statements(await File.ReadAllTextAsync(RecountStoredCountersDiagnosticTests.FilePath));
        await using var connection = new SqlConnection(api.ConnectionString);
        await connection.OpenAsync();
        return new Diagnosis(
            await Read(connection, statements[0], r => new SummaryRow(r.GetInt32(r.GetOrdinal("Step")), r.GetString(r.GetOrdinal("Counter")),
                r.GetInt32(r.GetOrdinal("RowsChecked")), r.GetInt32(r.GetOrdinal("RowsToChange")), r.GetInt32(r.GetOrdinal("StoredTooHigh")),
                r.GetInt32(r.GetOrdinal("StoredTooLow")), r.GetDecimal(r.GetOrdinal("TotalAbsoluteDrift")))),
            await Read(connection, statements[1], r => new ChangedRow(r.GetString(r.GetOrdinal("Counter")), r.GetString(r.GetOrdinal("RowId")),
                r.GetDecimal(r.GetOrdinal("Stored")), r.GetDecimal(r.GetOrdinal("Recounted")))),
            await Read(connection, statements[2], r => r.GetString(r.GetOrdinal("Place"))));
    }

    private static async Task<List<T>> Read<T>(SqlConnection connection, string sql, Func<DbDataReader, T> row)
    {
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(row(reader));
        }
        Assert.False(await reader.NextResultAsync(), "one statement, one result");
        return rows;
    }

    private static List<string> Sorted(IEnumerable<Value> values) =>
        values.Select(v => $"{v.Counter} {v.Id} {Text(v.Stored)}").Order(StringComparer.Ordinal).ToList();

    private static List<string> Sorted(IEnumerable<ChangedRow> rows) =>
        rows.Select(r => $"{r.Counter} {r.Id} {Text(r.Stored)} -> {Text(r.Recounted)}").Order(StringComparer.Ordinal).ToList();

    private static string Text(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
