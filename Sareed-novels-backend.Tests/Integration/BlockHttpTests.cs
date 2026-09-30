using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Blocking (POST /api/User/block and the rest) and what a block does, enforced by the API: the blocker's lists leave
/// out the blocked user's content, follows between them go, and the blocked user can't follow, answer, notify or open
/// the blocker.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public partial class BlockHttpTests(SardApiFactory api)
{
    private async Task<JsonElement> BlockedList(ApiUser user) => await (await api.Get("/api/User/blocked", user)).OkJson();

    [Fact]
    public async Task Blocking_and_unblocking_are_idempotent_and_the_list_has_the_current_names()
    {
        var (me, first, second) = (await api.SignUp(), await api.SignUp(), await api.SignUp());

        foreach (var response in new[] { await api.Block(me, first), await api.Block(me, first), await api.Block(me, second) })
        {
            var result = await response.OkJson();
            Assert.True(result.GetProperty("success").GetBoolean());
            Assert.True(result.GetProperty("isBlocked").GetBoolean());
            Assert.Equal("تم حظر المستخدم", result.GetProperty("message").GetString());
        }

        // The blocked user renames themselves: the list shows who they are now.
        var renamed = "r" + Guid.NewGuid().ToString("N")[..10];
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Patch, "/api/User/update-me", first,
            ReaderApi.Form(("UserName", renamed), ("DisplayName", "اسم جديد")))).StatusCode);

        var list = await BlockedList(me);
        Assert.Equal(2, list.GetProperty("totalItemsCount").GetInt32());
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([second.Id, first.Id], items.Select(i => i.GetProperty("userId").GetString())); // most recent first
        Assert.Equal(renamed, items[1].GetProperty("userName").GetString());
        Assert.Equal("اسم جديد", items[1].GetProperty("displayName").GetString());
        Assert.True(items[1].TryGetProperty("profilePhoto", out _));
        Assert.EndsWith("Z", items[1].GetProperty("blockedAt").GetString());

        foreach (var response in new[] { await api.Unblock(me, first), await api.Unblock(me, first) })
        {
            Assert.False((await response.OkJson()).GetProperty("isBlocked").GetBoolean());
        }
        // Also by route, for clients that can't send a body with DELETE.
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Delete, $"/api/User/block/{second.Id}", me)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Delete, $"/api/User/block/{second.Id}", me)).StatusCode);
        Assert.Equal(0, (await BlockedList(me)).GetProperty("totalItemsCount").GetInt32());
    }

    [Fact]
    public async Task Blocking_yourself_is_a_400_an_unknown_user_a_404_and_signing_in_is_needed()
    {
        var me = await api.SignUp();

        Assert.Equal("CannotBlockSelf", (await (await api.Block(me, me)).Error(HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        var unknown = me with { Id = Guid.NewGuid().ToString() };
        var notFound = await (await api.Block(me, unknown)).Error(HttpStatusCode.NotFound);
        Assert.Equal("UserNotFound", notFound.GetProperty("code").GetString());
        Assert.Equal("المستخدم غير موجود", notFound.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Unblock(me, unknown)).StatusCode);
        var missing = await api.Send(HttpMethod.Post, "/api/User/block", me, JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.Send(HttpMethod.Post, "/api/User/block", null, JsonContent.Create(new { userId = unknown.Id }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/User/blocked")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Send(HttpMethod.Delete, $"/api/User/block/{me.Id}")).StatusCode);
    }

    [Fact]
    public async Task Blocking_ends_follows_both_ways_and_neither_can_follow_the_other_until_unblocked()
    {
        var (me, them) = (await api.SignUp(), await api.SignUp());
        Assert.Equal(HttpStatusCode.OK, (await api.Follow(me, them)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Follow(them, me)).StatusCode);

        await api.Block(me, them);

        var mine = await (await api.Get($"/api/User/{me.UserName}")).OkJson();
        Assert.Equal(0, mine.GetProperty("totalFollowers").GetInt32());
        Assert.Equal(0, mine.GetProperty("totalFollowing").GetInt32());
        await using (var db = api.Db())
        {
            Assert.False(await db.Follows.AnyAsync(f => (f.FollowerId == me.Id && f.FollowedId == them.Id) || (f.FollowerId == them.Id && f.FollowedId == me.Id)));
        }

        var blocked = await (await api.Follow(them, me)).Error(HttpStatusCode.Forbidden);
        Assert.Equal("Blocked", blocked.GetProperty("code").GetString());
        Assert.Equal("لا يمكنك متابعة هذا المستخدم", blocked.GetProperty("message").GetString());
        var blocker = await (await api.Follow(me, them)).Error(HttpStatusCode.Forbidden);
        Assert.Equal("Blocked", blocker.GetProperty("code").GetString());

        await api.Unblock(me, them);
        Assert.Equal(HttpStatusCode.OK, (await api.Follow(them, me)).StatusCode);
    }

    [Fact]
    public async Task The_blockers_lists_leave_out_the_blocked_users_comments_replies_reviews_and_posts()
    {
        var (me, them, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(other);
        var (chapter, paragraphs) = await api.AddChapter(novel, "<p>فقرة</p>");
        var post = await api.Post(other);
        var lists = new[] { $"/api/comment/chapter/{chapter.Id}", $"/api/comment/paragraph/{paragraphs[0].Id}", $"/api/comment/post/{post}" };
        var theirs = new List<Guid>();
        var others = new List<Guid>();
        foreach (var url in lists)
        {
            theirs.Add(await api.Comment(them, url));
            others.Add(await api.Comment(other, url));
        }
        // A thread under someone else's comment, with a reply from each.
        var theirReply = await api.Comment(them, lists[0], "رد", parentId: others[0]);
        var otherReply = await api.Comment(other, lists[0], "رد", parentId: others[0]);
        var theirReview = await api.Review(them, novel.Id);
        var otherReview = await api.Review(me, novel.Id);
        await api.Post(them);

        await api.Block(me, them);

        for (var i = 0; i < lists.Length; i++)
        {
            var page = await (await api.Get(lists[i], me)).OkJson();
            Assert.Equal([others[i]], page.Ids());
            Assert.Equal(1, page.GetProperty("totalItemsCount").GetInt32());
            // Everyone else still sees both.
            Assert.Equal(2, (await (await api.Get(lists[i], other)).OkJson()).GetProperty("totalItemsCount").GetInt32());
            Assert.Equal(2, (await (await api.Get(lists[i])).OkJson()).GetProperty("totalItemsCount").GetInt32());
        }
        var thread = (await (await api.Get(lists[0], me)).OkJson()).GetProperty("items")[0];
        Assert.Equal(1, thread.GetProperty("totalRepliesCount").GetInt32());
        var replies = await (await api.Get($"/api/comment/chapter/comments/{others[0]}", me)).OkJson();
        Assert.Equal([otherReply], replies.Ids());
        Assert.Equal(1, replies.GetProperty("totalItemsCount").GetInt32());
        Assert.Equal(2, (await (await api.Get($"/api/comment/chapter/comments/{others[0]}")).OkJson()).GetProperty("totalItemsCount").GetInt32());
        var context = await (await api.Get($"/api/notifications/comment/{others[0]}", me)).OkJson();
        Assert.Equal([otherReply], context.Ids("replies"));

        var reviews = await (await api.Get($"/api/{novel.Id}?sorting=newest", me)).OkJson();
        Assert.DoesNotContain(theirReview, reviews.Ids("reviews"));
        Assert.Contains(otherReview, reviews.Ids("reviews"));
        Assert.Equal(1, reviews.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await (await api.Get($"/api/{novel.Id}?sorting=newest")).OkJson()).GetProperty("totalCount").GetInt32());

        var posts = await (await api.Get($"/api/posts/user/{them.Id}", me)).OkJson();
        Assert.Empty(posts.Ids());
        Assert.Single((await (await api.Get($"/api/posts/user/{them.Id}")).OkJson()).Ids());

        // Unblocking brings it all back.
        await api.Unblock(me, them);
        Assert.Equal(2, (await (await api.Get(lists[0], me)).OkJson()).GetProperty("totalItemsCount").GetInt32());
        Assert.Contains(theirReply, (await (await api.Get($"/api/comment/chapter/comments/{others[0]}", me)).OkJson()).Ids());
    }

    [Fact]
    public async Task The_blocked_user_cannot_answer_the_blockers_comments_or_comment_on_their_posts()
    {
        var (me, them, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (chapter, _) = await api.AddChapter(await api.AddNovel(me), "<p>فقرة</p>");
        var chapterUrl = $"/api/comment/chapter/{chapter.Id}";
        var myComment = await api.Comment(me, chapterUrl);
        var otherComment = await api.Comment(other, chapterUrl);
        var myPost = await api.Post(me);
        var otherOnMyPost = await api.Comment(other, $"/api/comment/post/{myPost}");

        await api.Block(me, them);

        var reply = await api.Send(HttpMethod.Post, chapterUrl, them, ReaderApi.Form(("Content", "رد"), ("ParentCommentId", myComment.ToString())));
        var refused = await reply.Error(HttpStatusCode.Forbidden);
        Assert.Equal("Blocked", refused.GetProperty("code").GetString());
        Assert.Equal("لا يمكنك الرد على تعليقات هذا المستخدم", refused.GetProperty("message").GetString());
        var onPost = await api.Send(HttpMethod.Post, $"/api/comment/post/{myPost}", them, ReaderApi.Form(("Content", "تعليق")));
        Assert.Equal("لا يمكنك التعليق على منشورات هذا المستخدم", (await onPost.Error(HttpStatusCode.Forbidden)).GetProperty("message").GetString());
        var replyOnPost = await api.Send(HttpMethod.Post, $"/api/comment/post/{myPost}", them,
            ReaderApi.Form(("Content", "رد"), ("ParentCommentId", otherOnMyPost.ToString())));
        Assert.Equal(HttpStatusCode.Forbidden, replyOnPost.StatusCode);

        // Elsewhere they can still take part: someone else's comment, and the blocker's chapter (the blocker won't see it).
        await api.Comment(them, chapterUrl, "رد", parentId: otherComment);
        await api.Comment(them, chapterUrl, "تعليق على الفصل");
        await using var db = api.Db();
        Assert.Equal(2, await db.Comments.CountAsync(c => c.UserId == them.Id));
    }

    [Fact]
    public async Task No_notification_from_the_blocked_user_reaches_the_blocker_and_earlier_ones_are_deleted()
    {
        var (me, them, control) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(me);
        var (chapter, _) = await api.AddChapter(novel, "<p>فقرة</p>");
        var chapterUrl = $"/api/comment/chapter/{chapter.Id}";
        var myComment = await api.Comment(me, chapterUrl);
        var myPost = await api.Post(me);
        var myList = await api.ReadingList(me);

        // Before the block: their follow notified me.
        await api.Follow(them, me);
        await api.WaitForNotificationsFrom(me, them);

        await api.Block(me, them);
        Assert.Empty(await api.NotificationsFrom(me, them));

        // Everything that notifies a user, done by them and then by someone else (whose notifications show when the
        // background work is done).
        foreach (var actor in new[] { them, control })
        {
            await api.Comment(actor, chapterUrl, "تعليق على فصلي");
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/comment/{myComment}/like", actor)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/posts/{myPost}/like", actor)).StatusCode);
            // My lists don't exist for them at all (#25), so they can't follow one.
            Assert.Equal(actor == them ? HttpStatusCode.NotFound : HttpStatusCode.OK,
                (await api.Send(HttpMethod.Post, $"/api/readinglist/{myList}/follow", actor)).StatusCode);
            await api.Review(actor, novel.Id);
        }
        var fromControl = await api.WaitForNotificationsFrom(me, control, count: 5);
        Assert.Equal(
            [NotificationType.CommentOnChapter, NotificationType.LikeOnComment, NotificationType.LikeOnPost, NotificationType.ReadingListFollowed, NotificationType.ReviewOnNovel],
            fromControl.Select(n => n.Type).Order());
        await Task.Delay(500);
        Assert.Empty(await api.NotificationsFrom(me, them));
    }

    [Fact]
    public async Task The_blocked_user_finds_no_blocker_while_the_blocker_sees_them_flagged()
    {
        var (me, them, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        await api.Post(me);
        await api.ReadingList(me);
        await api.ReadingList(them);

        await api.Block(me, them);

        // To them I don't exist: the same answer as for a name nobody has.
        var profile = await api.Get($"/api/User/{me.UserName}", them);
        Assert.Equal(HttpStatusCode.NotFound, profile.StatusCode);
        var nobody = await api.Get($"/api/User/nobody{Guid.NewGuid():N}", them);
        Assert.Equal(await nobody.Content.ReadAsStringAsync(), await profile.Content.ReadAsStringAsync());
        Assert.Empty((await (await api.Get($"/api/posts/user/{me.Id}", them)).OkJson()).Ids());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/readinglist/user/{me.UserName}", them)).StatusCode);

        // I still see them, flagged, so I can unblock; everyone else sees both of us as before.
        var theirs = await (await api.Get($"/api/User/{them.UserName}", me)).OkJson();
        Assert.True(theirs.GetProperty("isBlockedByMe").GetBoolean());
        Assert.Equal(0, (await (await api.Get($"/api/readinglist/user/{them.UserName}", me)).OkJson()).GetProperty("totalItemsCount").GetInt32());
        Assert.False((await (await api.Get($"/api/User/{them.UserName}", other)).OkJson()).GetProperty("isBlockedByMe").GetBoolean());
        Assert.False((await (await api.Get($"/api/User/{me.UserName}")).OkJson()).GetProperty("isBlockedByMe").GetBoolean());
        Assert.Single((await (await api.Get($"/api/posts/user/{me.Id}", other)).OkJson()).Ids());
        Assert.Equal(1, (await (await api.Get($"/api/readinglist/user/{them.UserName}", other)).OkJson()).GetProperty("totalItemsCount").GetInt32());

        await api.Unblock(me, them);
        Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/User/{me.UserName}", them)).StatusCode);
        Assert.False((await (await api.Get($"/api/User/{them.UserName}", me)).OkJson()).GetProperty("isBlockedByMe").GetBoolean());
    }

    [Fact]
    public async Task A_list_whose_owner_blocked_the_viewer_cannot_be_opened_or_followed_and_leaves_their_followed_lists()
    {
        var (me, them, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (myList, myPrivateList, myOtherList) = (await api.ReadingList(me), await api.ReadingList(me, isPublic: false), await api.ReadingList(me));
        var theirList = await api.ReadingList(them);
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/readinglist/{myList}/follow", them)).StatusCode);

        await api.Block(me, them);

        // To them my lists are gone: the answer for a list nobody has, a private one included (403 would say it exists).
        var gone = await api.Get($"/api/readinglist/{myList}", them);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        var missing = await api.Get($"/api/readinglist/{Guid.NewGuid()}", them);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await gone.Content.ReadAsStringAsync());
        Assert.Equal("ReadingListNotFound", (await gone.Error(HttpStatusCode.NotFound)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Get($"/api/readinglist/{myPrivateList}", them)).StatusCode);
        var follow = await api.Send(HttpMethod.Post, $"/api/readinglist/{myOtherList}/follow", them);
        Assert.Equal("ReadingListNotFound", (await follow.Error(HttpStatusCode.NotFound)).GetProperty("code").GetString());
        Assert.Equal(0, (await (await api.Get("/api/readinglist/followed", them)).OkJson()).GetProperty("totalItemsCount").GetInt32());
        await using (var db = api.Db())
        {
            Assert.False(await db.ReadingListFollowers.AnyAsync(f => f.ReadingListId == myOtherList && f.UserId == them.Id));
            Assert.Equal(1, (await db.ReadingLists.SingleAsync(l => l.Id == myList)).FollowersCount); // their follow stays, hidden
        }

        // I, everyone else and anonymous readers still open it, and I still open theirs.
        Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/readinglist/{myList}", me)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/readinglist/{myList}", other)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/readinglist/{myList}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Get($"/api/readinglist/{theirList}", me)).StatusCode);

        // Unblocking brings it back, with the follow they had.
        await api.Unblock(me, them);
        Assert.True((await (await api.Get($"/api/readinglist/{myList}", them)).OkJson()).GetProperty("isFollowing").GetBoolean());
        Assert.Equal(1, (await (await api.Get("/api/readinglist/followed", them)).OkJson()).GetProperty("totalItemsCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await api.Send(HttpMethod.Post, $"/api/readinglist/{myOtherList}/follow", them)).StatusCode);
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("My-Profile")]
    public async Task User_names_that_are_api_routes_are_refused(string userName)
    {
        var me = await api.SignUp();

        var rename = await api.Send(HttpMethod.Patch, "/api/User/update-me", me, ReaderApi.Form(("UserName", userName)));

        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        Assert.Contains("محجوز", await rename.Content.ReadAsStringAsync());
    }
}
