using System.Data.SqlTypes;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// A member's reviews and comments on their profile (#54): GET /api/User/{userName}/reviews and
/// GET /api/User/{userName}/comments, and the profile's reviewsCount and commentsCount (GET /api/User/{userName} and
/// my-profile), which are these lists' totals as anyone signed out sees them.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public partial class ProfileListsHttpTests(SardApiFactory api)
{
    private static string ReviewsOf(string userName, string query = "") => $"/api/User/{Uri.EscapeDataString(userName)}/reviews{query}";

    private static string CommentsOf(string userName, string query = "") => $"/api/User/{Uri.EscapeDataString(userName)}/comments{query}";

    private static string OnChapter(Guid chapterId) => $"/api/comment/chapter/{chapterId}";

    private static string OnParagraph(Guid paragraphId) => $"/api/comment/paragraph/{paragraphId}";

    private static string OnPost(Guid postId) => $"/api/comment/post/{postId}";

    private async Task<JsonElement> Page(string url, ApiUser? viewer = null) => await (await api.Get(url, viewer)).OkJson();

    private static JsonElement Item(JsonElement page, Guid id) =>
        page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);

    private static List<string> Names(JsonElement json) => json.EnumerateObject().Select(p => p.Name).ToList();

    /// <summary>Items made one after another through the API, newest first.</summary>
    private static List<Guid> Newest(IEnumerable<Guid> madeInOrder) => madeInOrder.Reverse().ToList();

    /// <summary>
    /// What anyone signed out finds on the member's lists is exactly <paramref name="reviews"/> and
    /// <paramref name="comments"/> (newest first), and the profile's counts, on GET /api/User/{userName} and on the
    /// member's my-profile, are these lists' totals.
    /// </summary>
    private async Task AssertListed(ApiUser member, IReadOnlyList<Guid> reviews, IReadOnlyList<Guid> comments)
    {
        var reviewPage = await Page(ReviewsOf(member.UserName, "?pageSize=50"));
        Assert.Equal(reviews, reviewPage.Ids());
        Assert.Equal(reviews.Count, reviewPage.GetProperty("totalItemsCount").GetInt32());
        var commentPage = await Page(CommentsOf(member.UserName, "?pageSize=50"));
        Assert.Equal(comments, commentPage.Ids());
        Assert.Equal(comments.Count, commentPage.GetProperty("totalItemsCount").GetInt32());

        foreach (var profile in new[] { await Page($"/api/User/{member.UserName}"), await Page("/api/User/my-profile", member) })
        {
            Assert.Equal(reviews.Count, profile.GetProperty("reviewsCount").GetInt32());
            Assert.Equal(comments.Count, profile.GetProperty("commentsCount").GetInt32());
        }
    }

    /// <summary>Chapter <paramref name="index"/> of the novel, published or a draft, with one paragraph; saved directly.</summary>
    private async Task<(Chapter Chapter, ChapterParagraph Paragraph)> AddChapter(Novel novel, int index, string status = ChapterStatuses.Published)
    {
        var chapter = Seed.Chapters(novel, 1, DateTime.UtcNow, status, startIndex: index).Single();
        chapter.Title = $"الفصل {index} {Seed.Marker()}";
        chapter.ParagraphsCount = 1;
        var paragraph = new ChapterParagraph
        {
            Id = Guid.NewGuid(),
            ChapterId = chapter.Id,
            Content = "<p>فقرة</p>",
            ContentHash = Guid.NewGuid().ToString("N"),
            OrderIndex = 0
        };

        await using var db = api.Db();
        db.Chapters.Add(chapter);
        db.ChapterParagraphs.Add(paragraph);
        await db.SaveChangesAsync();
        return (chapter, paragraph);
    }

    /// <summary>A novel by <paramref name="author"/> with a published chapter.</summary>
    private async Task<(Novel Novel, Chapter Chapter, ChapterParagraph Paragraph)> ReadableNovel(ApiUser author)
    {
        var novel = await api.AddNovel(author);
        var (chapter, paragraph) = await AddChapter(novel, 1);
        return (novel, chapter, paragraph);
    }

    private async Task<Guid> WriteReview(ApiUser reviewer, Guid novelId, bool isSpoiler)
    {
        var response = await api.Send(HttpMethod.Post, $"/api/{novelId}", reviewer, JsonContent.Create(new
        {
            writingQualityScore = 5, updatingStabilityScore = 4, characterDevelopmentScore = 3, worldBuildingScore = 2,
            isSpoiler, content = "رواية تستحق القراءة"
        }));
        return (await response.OkJson()).GetProperty("review").GetProperty("id").GetGuid();
    }

    private async Task Ok(HttpResponseMessage response) => await response.OkJson();

    private async Task RemovedByModerator(ApiUser reporter, ApiUser admin, string targetType, Guid id)
    {
        var report = await api.Reported(reporter, targetType, id);
        await Ok(await api.Resolve(admin, report, "RemoveContent"));
    }

    // ─── Paging ───

    [Fact]
    public async Task Pages_are_exact_newest_first_with_the_id_breaking_ties()
    {
        var (member, author) = (await api.SignUp(), await api.SignUp());
        var reviews = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            reviews.Add(await api.Review(member, (await ReadableNovel(author)).Novel.Id));
        }
        var (_, chapter, paragraph) = await ReadableNovel(author);
        var comments = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            comments.Add(await api.Comment(member, i % 2 == 0 ? OnChapter(chapter.Id) : OnParagraph(paragraph.Id), $"تعليق {i}"));
        }

        // Three written at each moment: the order within a moment is the id's, and pages of 5 cut through two moments.
        var start = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var at = new Dictionary<Guid, DateTime>();
        await using (var db = api.Db())
        {
            for (var i = 0; i < 12; i++)
            {
                var (review, comment, createdAt) = (reviews[i], comments[i], start.AddMinutes(i / 3));
                await db.Reviews.Where(r => r.Id == review).ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, createdAt));
                await db.Comments.Where(c => c.Id == comment).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, createdAt));
                at[review] = at[comment] = createdAt;
            }
        }

        // Newest first; within a moment by id, in SQL Server's uniqueidentifier order.
        List<Guid> Expected(IEnumerable<Guid> ids) => ids.OrderByDescending(id => at[id]).ThenBy(id => new SqlGuid(id)).ToList();

        foreach (var (list, ids) in new[] { (ReviewsOf(member.UserName), reviews), (CommentsOf(member.UserName), comments) })
        {
            var expected = Expected(ids);
            var paged = new List<Guid>();
            for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
            {
                var page = await Page($"{list}?pageNumber={pageNumber}&pageSize=5");
                Assert.Equal(expected.Skip((pageNumber - 1) * 5).Take(5), page.Ids());
                Assert.Equal(12, page.GetProperty("totalItemsCount").GetInt32());
                Assert.Equal(3, page.GetProperty("totalPages").GetInt32());
                Assert.Equal((pageNumber - 1) * 5 + 1, page.GetProperty("itemsFrom").GetInt32());
                Assert.Equal(Math.Min(pageNumber * 5, 12), page.GetProperty("itemsTo").GetInt32());
                paged.AddRange(page.Ids());
            }
            Assert.Equal(expected, paged);
            Assert.Empty((await Page($"{list}?pageNumber=4&pageSize=5")).Ids());

            // As the other lists: 10 a page by default, 1 to 50, from page 1.
            Assert.Equal(expected.Take(10), (await Page(list)).Ids());
            Assert.Equal(expected.Take(1), (await Page($"{list}?pageNumber=0&pageSize=0")).Ids());
            Assert.Equal(expected, (await Page($"{list}?pageSize=100000")).Ids());
        }

        await AssertListed(member, Expected(reviews), Expected(comments));
    }

    // ─── What is listed ───

    [Fact]
    public async Task Reviews_are_listed_on_novels_readers_can_open_and_only_there()
    {
        var (member, author, reporter, admin) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUpAdmin());
        var leaving = await api.SignUp();

        var kept = await ReadableNovel(author);
        var unpublished = await ReadableNovel(author);
        var drafted = await ReadableNovel(author);
        var deleted = await ReadableNovel(author);
        var removed = await ReadableNovel(author);
        var reported = await ReadableNovel(author);
        var orphaned = await ReadableNovel(leaving);
        // Never listed: a novel with no published chapter (a draft one only), and a draft novel.
        var withoutChapters = await api.AddNovel(author);
        await AddChapter(withoutChapters, 1, ChapterStatuses.Draft);
        var draft = await api.AddNovel(author, isDraft: true);
        await AddChapter(draft, 1);

        var listed = new List<Guid>();
        foreach (var novel in new[] { kept, unpublished, drafted, deleted, removed, reported, orphaned })
        {
            listed.Add(await api.Review(member, novel.Novel.Id));
        }
        var (onKept, onUnpublished, onDrafted, onDeleted, onRemoved, onReported, onOrphaned) =
            (listed[0], listed[1], listed[2], listed[3], listed[4], listed[5], listed[6]);
        await api.Review(member, withoutChapters.Id);
        await api.Review(member, draft.Id);
        await AssertListed(member, Newest(listed), []);

        // The author makes the only published chapter a draft again, in the editor: the novel has nothing left to read.
        await Ok(await api.Send(HttpMethod.Patch, $"/api/novel/{unpublished.Novel.Id}/chapter/{unpublished.Chapter.Id}", author,
            JsonContent.Create(new { status = ChapterStatuses.Draft, title = "فصل", content = "<p>نص</p>" })));
        listed.Remove(onUnpublished);
        await AssertListed(member, Newest(listed), []);

        // The author makes a novel a draft.
        await Ok(await api.Send(HttpMethod.Patch, $"/api/myworks/{drafted.Novel.Id}/draft", author));
        listed.Remove(onDrafted);
        await AssertListed(member, Newest(listed), []);

        // The author deletes a novel.
        await Ok(await api.Send(HttpMethod.Delete, $"/api/myworks/{deleted.Novel.Id}/delete", author));
        listed.Remove(onDeleted);
        await AssertListed(member, Newest(listed), []);

        // A moderator removes a reported novel, and a reported review.
        await RemovedByModerator(reporter, admin, "Novel", removed.Novel.Id);
        listed.Remove(onRemoved);
        await AssertListed(member, Newest(listed), []);
        await RemovedByModerator(reporter, admin, "Review", onReported);
        listed.Remove(onReported);
        await AssertListed(member, Newest(listed), []);

        // An author deletes their account: their novels are hidden with it.
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, "/api/User/me", leaving,
            JsonContent.Create(new { password = ModerationApi.Password }))).StatusCode);
        listed.Remove(onOrphaned);
        await AssertListed(member, [onKept], []);
        Assert.Equal([onKept], listed);
    }

    [Fact]
    public async Task Comments_are_listed_where_readers_can_read_them_and_only_there()
    {
        var (member, author, other, admin) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUpAdmin());
        var leaving = await api.SignUp();
        var (novel, chapter, paragraph) = await ReadableNovel(author);
        var (later, laterParagraph) = await AddChapter(novel, 2);
        var drafted = await ReadableNovel(author);
        var deleted = await ReadableNovel(author);
        var removed = await ReadableNovel(author);
        var orphaned = await ReadableNovel(leaving);

        // Listed: on a chapter, on a paragraph, and replies in another member's threads on each.
        var chapterThread = await api.Comment(other, OnChapter(chapter.Id), "رأي في الفصل");
        var paragraphThread = await api.Comment(other, OnParagraph(paragraph.Id), "رأي في الفقرة");
        var listed = new List<Guid>
        {
            await api.Comment(member, OnChapter(chapter.Id)),
            await api.Comment(member, OnParagraph(paragraph.Id)),
            await api.Comment(member, OnChapter(chapter.Id), "رد", chapterThread),
            await api.Comment(member, OnParagraph(paragraph.Id), "رد", paragraphThread)
        };
        var stays = listed.ToList();

        // Never listed: a comment on a post, a reply under a comment on a post, and one on a draft chapter.
        var post = await api.Post(other);
        var postThread = await api.Comment(other, OnPost(post), "على المنشور");
        await api.Comment(member, OnPost(post));
        await api.Comment(member, OnPost(post), "رد", postThread);
        var (draftChapter, _) = await AddChapter(novel, 3, ChapterStatuses.Draft);
        await api.Comment(member, OnChapter(draftChapter.Id));

        // Listed until what is done to each below.
        var deletedByMember = await api.Comment(member, OnChapter(chapter.Id));
        var thread = await api.Comment(other, OnChapter(chapter.Id), "سيُحذف");
        var replyInDeletedThread = await api.Comment(member, OnChapter(chapter.Id), "رد", thread);
        var onUnpublishedChapter = await api.Comment(member, OnChapter(later.Id));
        var onUnpublishedParagraph = await api.Comment(member, OnParagraph(laterParagraph.Id));
        var onDraftedNovel = await api.Comment(member, OnParagraph(drafted.Paragraph.Id));
        var onDeletedNovel = await api.Comment(member, OnChapter(deleted.Chapter.Id));
        var onRemovedNovel = await api.Comment(member, OnChapter(removed.Chapter.Id));
        var removedComment = await api.Comment(member, OnChapter(chapter.Id));
        var onOrphanedNovel = await api.Comment(member, OnChapter(orphaned.Chapter.Id));
        listed.AddRange([deletedByMember, replyInDeletedThread, onUnpublishedChapter, onUnpublishedParagraph, onDraftedNovel,
            onDeletedNovel, onRemovedNovel, removedComment, onOrphanedNovel]);
        await AssertListed(member, [], Newest(listed));

        // The member deletes a comment.
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, $"/api/comment/{deletedByMember}", member)).StatusCode);
        listed.Remove(deletedByMember);
        await AssertListed(member, [], Newest(listed));

        // The other member deletes the comment the member answered: the thread, the reply with it, is gone.
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, $"/api/comment/{thread}", other)).StatusCode);
        listed.Remove(replyInDeletedThread);
        await AssertListed(member, [], Newest(listed));

        // The chapter is a draft again: the comments on it and on its paragraph go (the novel stays readable). Set here
        // rather than through the editor, which also saves the text again and could replace the paragraph under test.
        await using (var db = api.Db())
        {
            await db.Chapters.Where(c => c.Id == later.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ChapterStatuses.Draft));
        }
        listed.Remove(onUnpublishedChapter);
        listed.Remove(onUnpublishedParagraph);
        await AssertListed(member, [], Newest(listed));

        // The author makes a novel a draft, and deletes another.
        await Ok(await api.Send(HttpMethod.Patch, $"/api/myworks/{drafted.Novel.Id}/draft", author));
        listed.Remove(onDraftedNovel);
        await AssertListed(member, [], Newest(listed));
        await Ok(await api.Send(HttpMethod.Delete, $"/api/myworks/{deleted.Novel.Id}/delete", author));
        listed.Remove(onDeletedNovel);
        await AssertListed(member, [], Newest(listed));

        // A moderator removes a reported novel, and a reported comment.
        await RemovedByModerator(other, admin, "Novel", removed.Novel.Id);
        listed.Remove(onRemovedNovel);
        await AssertListed(member, [], Newest(listed));
        await RemovedByModerator(other, admin, "Comment", removedComment);
        listed.Remove(removedComment);
        await AssertListed(member, [], Newest(listed));

        // An author deletes their account: their novels are hidden with it.
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, "/api/User/me", leaving,
            JsonContent.Create(new { password = ModerationApi.Password }))).StatusCode);
        listed.Remove(onOrphanedNovel);
        await AssertListed(member, [], Newest(stays));
        Assert.Equal(stays, listed);
    }

    // ─── The items ───

    [Fact]
    public async Task Items_have_the_review_or_comment_where_it_was_written_and_the_viewers_like()
    {
        var (member, author, viewer, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var (first, _) = await AddChapter(novel, 1);
        await AddChapter(novel, 2, ChapterStatuses.Draft);
        var (third, paragraph) = await AddChapter(novel, 3);
        var (otherNovel, _, _) = await ReadableNovel(author);

        // A spoiler, edited once; and a plain review nobody likes.
        var spoiler = await WriteReview(member, novel.Id, isSpoiler: true);
        await Ok(await api.Send(HttpMethod.Patch, $"/api/{novel.Id}/reviews/{spoiler}", member,
            JsonContent.Create(new { content = "رواية تستحق القراءة حقًا" })));
        var plain = await api.Review(member, otherNovel.Id);

        // On the first chapter with a picture, on a paragraph of the third, and a reply in another member's thread there.
        using var form = ReaderApi.Form(("Content", "على الفصل"));
        var image = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "AttachedImage", "photo.png");
        var onChapter = (await (await api.Send(HttpMethod.Post, OnChapter(first.Id), member, form)).OkJson())
            .GetProperty("comment").GetProperty("id").GetGuid();
        var onParagraph = await api.Comment(member, OnParagraph(paragraph.Id), "على الفقرة");
        var thread = await api.Comment(other, OnParagraph(paragraph.Id), "رأي");
        var reply = await api.Comment(member, OnParagraph(paragraph.Id), "رد على الرأي", thread);

        // The viewer likes the spoiler and the reply.
        await Ok(await api.Send(HttpMethod.Post, $"/api/{novel.Id}/reviews/{spoiler}/like", viewer));
        await Ok(await api.Send(HttpMethod.Post, $"/api/comment/{reply}/like", viewer));

        var reviews = await Page(ReviewsOf(member.UserName), viewer);
        Assert.Equal(["items", "totalPages", "totalItemsCount", "itemsFrom", "itemsTo"], Names(reviews));
        Assert.Equal([plain, spoiler], reviews.Ids());
        var review = Item(reviews, spoiler);
        Assert.Equal(["id", "writingQualityScore", "updatingStabilityScore", "characterDevelopmentScore", "worldBuildingScore",
            "totalAverageScore", "content", "isSpoiler", "likeCount", "isLikedByCurrentUser", "createdAt", "updatedAt", "novel"], Names(review));
        Assert.Equal(5m, review.GetProperty("writingQualityScore").GetDecimal());
        Assert.Equal(4m, review.GetProperty("updatingStabilityScore").GetDecimal());
        Assert.Equal(3m, review.GetProperty("characterDevelopmentScore").GetDecimal());
        Assert.Equal(2m, review.GetProperty("worldBuildingScore").GetDecimal());
        Assert.Equal(3.5m, review.GetProperty("totalAverageScore").GetDecimal());
        Assert.Equal("رواية تستحق القراءة حقًا", review.GetProperty("content").GetString());
        Assert.True(review.GetProperty("isSpoiler").GetBoolean());
        Assert.Equal(1, review.GetProperty("likeCount").GetInt32());
        Assert.True(review.GetProperty("isLikedByCurrentUser").GetBoolean());
        Assert.EndsWith("Z", review.GetProperty("createdAt").GetString());
        Assert.EndsWith("Z", review.GetProperty("updatedAt").GetString());
        var reviewedNovel = review.GetProperty("novel");
        Assert.Equal(["id", "slug", "title", "coverImageUrl"], Names(reviewedNovel));
        Assert.Equal(novel.Id, reviewedNovel.GetProperty("id").GetGuid());
        Assert.Equal(novel.Slug, reviewedNovel.GetProperty("slug").GetString());
        Assert.Equal(novel.Title, reviewedNovel.GetProperty("title").GetString());
        Assert.Equal(novel.CoverImageUrl, reviewedNovel.GetProperty("coverImageUrl").GetString());
        var unliked = Item(reviews, plain);
        Assert.False(unliked.GetProperty("isSpoiler").GetBoolean());
        Assert.False(unliked.GetProperty("isLikedByCurrentUser").GetBoolean());
        Assert.Equal(JsonValueKind.Null, unliked.GetProperty("updatedAt").ValueKind);
        Assert.Equal(otherNovel.Id, unliked.GetProperty("novel").GetProperty("id").GetGuid());

        var comments = await Page(CommentsOf(member.UserName), viewer);
        Assert.Equal(["items", "totalPages", "totalItemsCount", "itemsFrom", "itemsTo"], Names(comments));
        Assert.Equal([reply, onParagraph, onChapter], comments.Ids());
        var answer = Item(comments, reply);
        Assert.Equal(["id", "content", "attachedImageUrl", "likesCount", "isLikedByCurrentUser", "createdAt", "updatedAt",
            "isReply", "parentCommentId", "parentComment", "novel", "chapter", "paragraphId", "paragraphExcerpt"], Names(answer));
        Assert.Equal("رد على الرأي", answer.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("attachedImageUrl").ValueKind);
        Assert.Equal(1, answer.GetProperty("likesCount").GetInt32());
        Assert.True(answer.GetProperty("isLikedByCurrentUser").GetBoolean());
        Assert.EndsWith("Z", answer.GetProperty("createdAt").GetString());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("updatedAt").ValueKind);
        Assert.True(answer.GetProperty("isReply").GetBoolean());
        Assert.Equal(thread, answer.GetProperty("parentCommentId").GetGuid());
        // What it answers (#60): the other member's comment, by them, as its thread shows it (#67).
        var answered = answer.GetProperty("parentComment");
        Assert.Equal(ParentFields, Names(answered));
        Assert.Equal(thread, answered.GetProperty("id").GetGuid());
        Assert.Equal("رأي", answered.GetProperty("content").GetString());
        Assert.Equal(["id", "userName", "displayName", "profilePhoto"], Names(answered.GetProperty("user")));
        Assert.Equal(other.Id, answered.GetProperty("user").GetProperty("id").GetString());
        Assert.Equal(["id", "slug", "title", "coverImageUrl"], Names(answer.GetProperty("novel")));
        Assert.Equal(novel.Id, answer.GetProperty("novel").GetProperty("id").GetGuid());
        Assert.Equal(novel.Slug, answer.GetProperty("novel").GetProperty("slug").GetString());
        Assert.Equal(novel.Title, answer.GetProperty("novel").GetProperty("title").GetString());
        // The paragraph's chapter: the third, which readers number 2 (the second is a draft).
        var answeredIn = answer.GetProperty("chapter");
        Assert.Equal(["id", "title", "number"], Names(answeredIn));
        Assert.Equal(third.Id, answeredIn.GetProperty("id").GetGuid());
        Assert.Equal(third.Title, answeredIn.GetProperty("title").GetString());
        Assert.Equal(2, answeredIn.GetProperty("number").GetInt32());
        Assert.Equal(paragraph.Id, answer.GetProperty("paragraphId").GetGuid());
        Assert.Equal("فقرة", answer.GetProperty("paragraphExcerpt").GetString());

        var onParagraphItem = Item(comments, onParagraph);
        Assert.False(onParagraphItem.GetProperty("isReply").GetBoolean());
        Assert.Equal(JsonValueKind.Null, onParagraphItem.GetProperty("parentCommentId").ValueKind);
        Assert.Equal(JsonValueKind.Null, onParagraphItem.GetProperty("parentComment").ValueKind);
        Assert.Equal("فقرة", onParagraphItem.GetProperty("paragraphExcerpt").GetString());
        Assert.False(onParagraphItem.GetProperty("isLikedByCurrentUser").GetBoolean());
        Assert.Equal(paragraph.Id, onParagraphItem.GetProperty("paragraphId").GetGuid());
        Assert.Equal(third.Id, onParagraphItem.GetProperty("chapter").GetProperty("id").GetGuid());

        var onChapterItem = Item(comments, onChapter);
        Assert.StartsWith("https://files.test/comment-images/", onChapterItem.GetProperty("attachedImageUrl").GetString());
        Assert.Equal(JsonValueKind.Null, onChapterItem.GetProperty("paragraphId").ValueKind);
        Assert.Equal(JsonValueKind.Null, onChapterItem.GetProperty("paragraphExcerpt").ValueKind);
        Assert.Equal(JsonValueKind.Null, onChapterItem.GetProperty("parentComment").ValueKind);
        Assert.Equal(first.Id, onChapterItem.GetProperty("chapter").GetProperty("id").GetGuid());
        Assert.Equal(1, onChapterItem.GetProperty("chapter").GetProperty("number").GetInt32());

        // Anyone else, signed in or not, and the member, get the same items, liked by nobody.
        foreach (var someone in new[] { null, other, member })
        {
            var theirReviews = await Page(ReviewsOf(member.UserName), someone);
            Assert.Equal([plain, spoiler], theirReviews.Ids());
            Assert.All(theirReviews.GetProperty("items").EnumerateArray(), r => Assert.False(r.GetProperty("isLikedByCurrentUser").GetBoolean()));
            Assert.True(Item(theirReviews, spoiler).GetProperty("isSpoiler").GetBoolean());
            Assert.Equal(1, Item(theirReviews, spoiler).GetProperty("likeCount").GetInt32());
            var theirComments = await Page(CommentsOf(member.UserName), someone);
            Assert.Equal([reply, onParagraph, onChapter], theirComments.Ids());
            Assert.All(theirComments.GetProperty("items").EnumerateArray(), c => Assert.False(c.GetProperty("isLikedByCurrentUser").GetBoolean()));
        }

        await AssertListed(member, [plain, spoiler], [reply, onParagraph, onChapter]);
    }

    // ─── Blocks ───

    [Fact]
    public async Task A_block_either_way_empties_both_lists_for_the_two_and_for_no_one_else()
    {
        var (member, viewer, other, author) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (novel, chapter, _) = await ReadableNovel(author);
        var review = await api.Review(member, novel.Id);
        var comment = await api.Comment(member, OnChapter(chapter.Id));

        async Task AssertEmptyFor(ApiUser someone)
        {
            foreach (var list in new[] { ReviewsOf(member.UserName), CommentsOf(member.UserName) })
            {
                var page = await Page(list, someone);
                Assert.Empty(page.Ids());
                Assert.Equal(0, page.GetProperty("totalItemsCount").GetInt32());
                Assert.Equal(0, page.GetProperty("totalPages").GetInt32());
            }
        }

        async Task AssertAllFor(ApiUser? someone)
        {
            Assert.Equal([review], (await Page(ReviewsOf(member.UserName), someone)).Ids());
            Assert.Equal([comment], (await Page(CommentsOf(member.UserName), someone)).Ids());
        }

        await AssertAllFor(viewer);

        // The viewer blocks the member: the lists are empty for the viewer, who still opens the profile, flagged, with
        // the counts anyone sees.
        await Ok(await api.Block(viewer, member));
        await AssertEmptyFor(viewer);
        foreach (var someone in new[] { null, other, member })
        {
            await AssertAllFor(someone);
        }
        var profile = await Page($"/api/User/{member.UserName}", viewer);
        Assert.True(profile.GetProperty("isBlockedByMe").GetBoolean());
        Assert.Equal(1, profile.GetProperty("reviewsCount").GetInt32());
        Assert.Equal(1, profile.GetProperty("commentsCount").GetInt32());
        await AssertListed(member, [review], [comment]);

        await Ok(await api.Unblock(viewer, member));
        await AssertAllFor(viewer);

        // The member blocks the viewer: empty for the viewer again (whose profile of the member is a 404 now).
        await Ok(await api.Block(member, viewer));
        await AssertEmptyFor(viewer);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/User/{member.UserName}", viewer)).StatusCode);
        foreach (var someone in new[] { null, other, member })
        {
            await AssertAllFor(someone);
        }
        await AssertListed(member, [review], [comment]);
    }

    // ─── Finding the member ───

    [Fact]
    public async Task The_member_is_found_as_the_profile_finds_them_by_an_old_name_too_and_never_once_deleted()
    {
        var (member, author) = (await api.SignUp(), await api.SignUp());
        await AssertListed(member, [], []);
        var (novel, chapter, _) = await ReadableNovel(author);
        var review = await api.Review(member, novel.Id);
        var comment = await api.Comment(member, OnChapter(chapter.Id));

        // Renamed: the old name finds the member too, as the profile does, and any letter case does.
        var oldName = member.UserName;
        var newName = "r" + Guid.NewGuid().ToString("N")[..10];
        await Ok(await api.Send(HttpMethod.Patch, "/api/User/update-me", member, ReaderApi.Form(("UserName", newName))));
        member = member with { UserName = newName };
        foreach (var name in new[] { oldName, newName, newName.ToUpperInvariant() })
        {
            Assert.Equal([review], (await Page(ReviewsOf(name))).Ids());
            Assert.Equal([comment], (await Page(CommentsOf(name))).Ids());
        }
        await AssertListed(member, [review], [comment]);

        async Task AssertNotFound(string userName)
        {
            foreach (var list in new[] { ReviewsOf(userName), CommentsOf(userName), $"/api/User/{Uri.EscapeDataString(userName)}" })
            {
                var error = await (await api.Get(list)).Error(HttpStatusCode.NotFound);
                Assert.Equal("UserNotFound", error.GetProperty("code").GetString());
                Assert.Equal("المستخدم غير موجود", error.GetProperty("message").GetString());
            }
        }

        await AssertNotFound("nobody" + Guid.NewGuid().ToString("N")[..10]);

        // Deleted: neither its old names nor the one it has now ("deleted-...") find the account.
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, "/api/User/me", member,
            JsonContent.Create(new { password = ModerationApi.Password }))).StatusCode);
        string deletedName;
        await using (var db = api.Db())
        {
            deletedName = await db.Users.Where(u => u.Id == member.Id).Select(u => u.UserName!).SingleAsync();
        }
        Assert.StartsWith("deleted-", deletedName);
        foreach (var name in new[] { newName, oldName, deletedName })
        {
            await AssertNotFound(name);
        }
    }
}
