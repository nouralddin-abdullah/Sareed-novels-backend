using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #52: a single post, its discussion, likes, comments on posts, replies and notifications respect a block, as the
/// lists already did. To a member the author blocked, a post and its discussion are unavailable (404 PostUnavailable),
/// while a member who blocked the author still opens the post, flagged; neither can like, comment on or reply to the
/// other's content (403 Blocked), and no notification passes between them.
/// </summary>
public partial class BlockHttpTests
{
    private const string LikeRefused = "لا يمكنك التفاعل مع هذا المستخدم.";

    private static async Task AssertUnavailable(HttpResponseMessage response)
    {
        var error = await response.Error(HttpStatusCode.NotFound);
        Assert.Equal("PostUnavailable", error.GetProperty("code").GetString());
        Assert.Equal("هذا المنشور غير متاح", error.GetProperty("message").GetString());
    }

    private async Task<bool> AuthorBlockedByMe(Guid post, ApiUser? viewer) =>
        (await (await api.Get($"/api/posts/{post}", viewer)).OkJson()).GetProperty("authorBlockedByMe").GetBoolean();

    [Fact]
    public async Task A_post_is_unavailable_to_a_member_its_author_blocked_and_flagged_for_one_who_blocked_the_author()
    {
        var (author, blockedByAuthor, blockingAuthor) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (both, other) = (await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);
        var deleted = await api.Post(author);
        var delete = await api.Send(HttpMethod.Delete, $"/api/posts/{deleted}", author);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        await api.Block(author, blockedByAuthor);
        await api.Block(blockingAuthor, author);
        await api.Block(author, both);
        await api.Block(both, author);

        // The author blocked them (both ways included): unavailable, not the PostNotFound of a deleted post...
        await AssertUnavailable(await api.Get($"/api/posts/{post}", blockedByAuthor));
        await AssertUnavailable(await api.Get($"/api/posts/{post}", both));
        // ...which a deleted post still is, to everyone.
        foreach (var viewer in new[] { blockedByAuthor, blockingAuthor, other, null })
        {
            var gone = await (await api.Get($"/api/posts/{deleted}", viewer)).Error(HttpStatusCode.NotFound);
            Assert.Equal("PostNotFound", gone.GetProperty("code").GetString());
        }

        // They blocked the author: the post, flagged, so they can unblock.
        var flagged = await (await api.Get($"/api/posts/{post}", blockingAuthor)).OkJson();
        Assert.Equal(post, flagged.GetProperty("id").GetGuid());
        Assert.True(flagged.GetProperty("authorBlockedByMe").GetBoolean());

        // The author, everyone else and anonymous readers: as before, not flagged.
        Assert.False(await AuthorBlockedByMe(post, author));
        Assert.False(await AuthorBlockedByMe(post, other));
        Assert.False(await AuthorBlockedByMe(post, null));

        // Unblocking brings it back; a block left the other way still counts.
        await api.Unblock(author, blockedByAuthor);
        await api.Unblock(blockingAuthor, author);
        await api.Unblock(author, both);
        Assert.False(await AuthorBlockedByMe(post, blockedByAuthor));
        Assert.False(await AuthorBlockedByMe(post, blockingAuthor));
        Assert.True(await AuthorBlockedByMe(post, both));
    }

    [Fact]
    public async Task Every_answer_with_a_post_says_whether_its_author_is_blocked_by_the_viewer()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());

        var create = await api.Send(HttpMethod.Post, "/api/posts", author, ReaderApi.Form(("Content", "منشور")));
        Assert.False((await create.OkJson()).GetProperty("post").GetProperty("authorBlockedByMe").GetBoolean());
        var listed = await (await api.Get($"/api/posts/user/{author.Id}", reader)).OkJson();
        var item = Assert.Single(listed.GetProperty("items").EnumerateArray());
        Assert.False(item.GetProperty("authorBlockedByMe").GetBoolean());

        // A reader who blocked the author gets no list at all (as before #52), so no item can say false to them.
        await api.Block(reader, author);
        Assert.Empty((await (await api.Get($"/api/posts/user/{author.Id}", reader)).OkJson()).Ids());
    }

    [Fact]
    public async Task A_posts_discussion_is_unavailable_to_a_member_its_author_blocked()
    {
        var (author, blockedByAuthor) = (await api.SignUp(), await api.SignUp());
        var (blockingAuthor, other) = (await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);
        var comments = $"/api/comment/post/{post}";
        var comment = await api.Comment(other, comments);
        var reply = await api.Comment(other, comments, "رد", parentId: comment);
        // Read per post, per comment's thread, and per comment with its context (from a notification).
        var reads = new[]
        {
            comments, $"/api/comment/chapter/comments/{comment}",
            $"/api/notifications/comment/{comment}", $"/api/notifications/comment/{reply}"
        };

        await api.Block(author, blockedByAuthor);
        await api.Block(blockingAuthor, author);

        foreach (var url in reads)
        {
            await AssertUnavailable(await api.Get(url, blockedByAuthor));
            // The member who blocked the author, everyone else and anonymous readers read it as before.
            foreach (var viewer in new[] { blockingAuthor, other, null })
            {
                await (await api.Get(url, viewer)).OkJson();
            }
        }
        Assert.Equal([comment], (await (await api.Get(comments, blockingAuthor)).OkJson()).Ids());

        await api.Unblock(author, blockedByAuthor);
        foreach (var url in reads)
        {
            await (await api.Get(url, blockedByAuthor)).OkJson();
        }
    }

    /// <summary>Something to like: what it is, its id, and its like and unlike URLs.</summary>
    private sealed record Likeable(string Kind, Guid Id, string Like, string Unlike);

    /// <summary>A post, a comment and a review by <paramref name="owner"/> (a review on a novel of its own).</summary>
    private async Task<Likeable[]> ContentBy(ApiUser owner, ApiUser novelist, string chapterComments)
    {
        var post = await api.Post(owner);
        var comment = await api.Comment(owner, chapterComments);
        var novel = await api.AddNovel(novelist);
        var review = await api.Review(owner, novel.Id);
        return
        [
            new("post", post, $"/api/posts/{post}/like", $"/api/posts/{post}/unlike"),
            new("comment", comment, $"/api/comment/{comment}/like", $"/api/comment/{comment}/unlike"),
            new("review", review, $"/api/{novel.Id}/reviews/{review}/like", $"/api/{novel.Id}/reviews/{review}/unlike")
        ];
    }

    /// <summary>Whether <paramref name="user"/>'s like of the item is stored, and the item's like count.</summary>
    private async Task<(bool Liked, int Count)> LikeOf(Likeable item, ApiUser user)
    {
        await using var db = api.Db();
        return item.Kind switch
        {
            "post" => (await db.PostLikes.AnyAsync(l => l.PostId == item.Id && l.UserId == user.Id),
                await db.Posts.Where(p => p.Id == item.Id).Select(p => p.LikesCount).SingleAsync()),
            "comment" => (await db.CommentLikes.AnyAsync(l => l.CommentId == item.Id && l.UserId == user.Id),
                await db.Comments.Where(c => c.Id == item.Id).Select(c => c.LikesCount).SingleAsync()),
            _ => (await db.ReviewLikes.AnyAsync(l => l.ReviewId == item.Id && l.UserId == user.Id),
                await db.Reviews.Where(r => r.Id == item.Id).Select(r => r.LikeCount).SingleAsync())
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_like_is_refused_whichever_of_the_two_blocked_the_other_and_an_earlier_one_can_be_taken_back(
        bool ownerBlocked)
    {
        var (owner, liker, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (chapter, _) = await api.AddChapter(await api.AddNovel(other), "<p>فقرة</p>");
        var chapterComments = $"/api/comment/chapter/{chapter.Id}";
        var likedBefore = await ContentBy(owner, other, chapterComments);
        var notLiked = await ContentBy(owner, other, chapterComments);
        foreach (var item in likedBefore)
        {
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, item.Like, liker)).StatusCode);
        }
        await api.WaitForNotificationsFrom(owner, liker, count: likedBefore.Length);

        await (ownerBlocked ? api.Block(owner, liker) : api.Block(liker, owner));

        foreach (var item in notLiked)
        {
            var refused = await (await api.Send(HttpMethod.Post, item.Like, liker)).Error(HttpStatusCode.Forbidden);
            Assert.Equal("Blocked", refused.GetProperty("code").GetString());
            Assert.Equal(LikeRefused, refused.GetProperty("message").GetString());
            // Nothing was written: no like, no count.
            Assert.Equal((false, 0), await LikeOf(item, liker));
        }

        // A like from before the block can still be taken back, and again (204).
        foreach (var item in likedBefore)
        {
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Delete, item.Unlike, liker)).StatusCode);
            Assert.Equal((false, 0), await LikeOf(item, liker));
            Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, item.Unlike, liker)).StatusCode);
        }

        // Anyone else still likes them, and is notified of (background work that is done once these show)...
        foreach (var item in notLiked)
        {
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, item.Like, other)).StatusCode);
        }
        await api.WaitForNotificationsFrom(owner, other, count: notLiked.Length);
        // ...while the refused likes notified nobody.
        await Task.Delay(500);
        var refusedIds = notLiked.Select(i => (Guid?)i.Id).ToList();
        Assert.DoesNotContain(await api.NotificationsFrom(owner, liker), n => refusedIds.Contains(n.RelatedEntityId));

        // Liking one's own content is as before: a post can be, a comment or review can't.
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, notLiked[0].Like, owner)).StatusCode);
        var own = await (await api.Send(HttpMethod.Post, notLiked[1].Like, owner)).Error(HttpStatusCode.BadRequest);
        Assert.Equal("CannotLikeOwnContent", own.GetProperty("code").GetString());

        // After an unblock, liking works again.
        await (ownerBlocked ? api.Unblock(owner, liker) : api.Unblock(liker, owner));
        foreach (var item in notLiked)
        {
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, item.Like, liker)).StatusCode);
            Assert.True((await LikeOf(item, liker)).Liked);
        }
    }

    [Fact]
    public async Task The_blocker_can_neither_comment_on_the_blocked_members_posts_nor_reply_to_their_comments()
    {
        var (me, them, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (chapter, paragraphs) = await api.AddChapter(await api.AddNovel(other), "<p>فقرة</p>");
        var chapterComments = $"/api/comment/chapter/{chapter.Id}";
        var paragraphComments = $"/api/comment/paragraph/{paragraphs[0].Id}";
        var theirPostComments = $"/api/comment/post/{await api.Post(them)}";
        var otherPostComments = $"/api/comment/post/{await api.Post(other)}";
        var otherOnTheirPost = await api.Comment(other, theirPostComments);
        var theirOnChapter = await api.Comment(them, chapterComments);
        var theirOnParagraph = await api.Comment(them, paragraphComments);
        var theirOnOtherPost = await api.Comment(them, otherPostComments);
        var otherOnChapter = await api.Comment(other, chapterComments);

        await api.Block(me, them);

        const string commentOnPost = "ألغِ حظر هذا المستخدم أولاً لتتمكن من التعليق على منشوراته";
        const string replyToComment = "ألغِ حظر هذا المستخدم أولاً لتتمكن من الرد على تعليقاته";
        var refused = new (string Url, Guid? Parent, string Message)[]
        {
            (theirPostComments, null, commentOnPost),
            (theirPostComments, otherOnTheirPost, commentOnPost), // a reply under their post, to someone else
            (chapterComments, theirOnChapter, replyToComment),
            (paragraphComments, theirOnParagraph, replyToComment),
            (otherPostComments, theirOnOtherPost, replyToComment) // their comment on someone else's post
        };
        foreach (var (url, parent, message) in refused)
        {
            var fields = parent is { } id
                ? new[] { ("Content", "تعليق"), ("ParentCommentId", id.ToString()) }
                : [("Content", "تعليق")];
            var response = await api.Send(HttpMethod.Post, url, me, ReaderApi.Form(fields));
            var error = await response.Error(HttpStatusCode.Forbidden);
            Assert.Equal("Blocked", error.GetProperty("code").GetString());
            Assert.Equal(message, error.GetProperty("message").GetString());
        }
        await using (var db = api.Db())
        {
            Assert.False(await db.Comments.IgnoreQueryFilters().AnyAsync(c => c.UserId == me.Id));
        }

        // A novel's discussion stays open to both: top-level comments on a chapter or a paragraph, and replies to
        // others.
        foreach (var url in new[] { chapterComments, paragraphComments })
        {
            await api.Comment(me, url);
            await api.Comment(them, url);
        }
        await api.Comment(me, chapterComments, "رد", parentId: otherOnChapter);

        // After unblocking, both work again.
        await api.Unblock(me, them);
        await api.Comment(me, theirPostComments);
        await api.Comment(me, chapterComments, "رد", parentId: theirOnChapter);
    }
}
