using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #67: in a member's comment list (GET /api/User/{userName}/comments), the comment a reply answers
/// (<c>parentComment</c>, #60) has what the chapter and paragraph comment lists give a comment, under their names:
/// <c>attachedImageUrl</c>, <c>likesCount</c>, <c>isLikedByCurrentUser</c>, <c>createdAt</c> (UTC with "Z") and
/// <c>totalRepliesCount</c>, read with the page in the same SQL commands as before.
/// </summary>
public partial class ProfileListsHttpTests
{
    /// <summary>The fields of a <c>parentComment</c> since #67.</summary>
    private static readonly string[] ParentFields =
        ["id", "content", "attachedImageUrl", "likesCount", "isLikedByCurrentUser", "createdAt", "totalRepliesCount", "user"];

    /// <summary>A date the API sent, as the UTC instant it names (the comment lists send UTC without a "Z").</summary>
    private static DateTime Instant(JsonElement date) =>
        DateTime.Parse(date.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    /// <summary>A comment with a picture, posted through the API at <paramref name="url"/>.</summary>
    private async Task<Guid> CommentWithPicture(ApiUser author, string url, string content)
    {
        using var form = ReaderApi.Form(("Content", content));
        var image = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "AttachedImage", "photo.png");
        return (await (await api.Send(HttpMethod.Post, url, author, form)).OkJson()).GetProperty("comment").GetProperty("id").GetGuid();
    }

    private async Task Like(ApiUser user, Guid comment) => await Ok(await api.Send(HttpMethod.Post, $"/api/comment/{comment}/like", user));

    /// <summary>
    /// <paramref name="parent"/> (a listed reply's <c>parentComment</c>) is <paramref name="inThread"/> (the same
    /// comment in its chapter's or paragraph's comment list, as the same viewer gets it): every field the same, the time
    /// the same instant, sent in UTC with "Z".
    /// </summary>
    private static void AssertSameComment(JsonElement inThread, JsonElement parent)
    {
        Assert.Equal(ParentFields, Names(parent));
        foreach (var field in ParentFields.Where(f => f != "createdAt"))
        {
            Assert.Equal(inThread.GetProperty(field).GetRawText(), parent.GetProperty(field).GetRawText());
        }
        Assert.EndsWith("Z", parent.GetProperty("createdAt").GetString());
        Assert.Equal(Instant(inThread.GetProperty("createdAt")), Instant(parent.GetProperty("createdAt")));
    }

    [Fact]
    public async Task A_parent_has_its_picture_likes_time_and_replies_as_its_thread_list_gives_them()
    {
        var (member, other, liker, someone, replier, blocker) =
            (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (_, chapter, paragraph) = await ReadableNovel(await api.SignUp());

        // On the chapter with a picture, liked by two members, with replies by the member and by someone else (and one
        // deleted since); on the paragraph without a picture, liked by the member, with the member's reply.
        var pictured = await CommentWithPicture(other, OnChapter(chapter.Id), "رأي مع صورة");
        var plain = await api.Comment(other, OnParagraph(paragraph.Id), "رأي بلا صورة");
        var toPictured = await api.Comment(member, OnChapter(chapter.Id), "رد على الصورة", pictured);
        var toPlain = await api.Comment(member, OnParagraph(paragraph.Id), "رد على الفقرة", plain);
        await api.Comment(replier, OnChapter(chapter.Id), "رد آخر", pictured);
        var deletedReply = await api.Comment(someone, OnChapter(chapter.Id), "رد محذوف", pictured);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, $"/api/comment/{deletedReply}", someone)).StatusCode);
        await Like(liker, pictured);
        await Like(someone, pictured);
        await Like(member, plain);
        // Written at a known moment, to the tick, so the time sent can be read exactly.
        var writtenAt = new DateTime(2026, 9, 30, 8, 15, 42, DateTimeKind.Utc).AddTicks(1234567);
        await using (var db = api.Db())
        {
            await db.Comments.Where(c => c.Id == pictured).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, writtenAt));
        }

        // The blocker blocked the other replier: neither the thread nor the parent counts that reply for them.
        await Ok(await api.Block(blocker, replier));

        foreach (var (viewer, likesPictured, likesPlain, repliesToPictured) in new (ApiUser?, bool, bool, int)[]
                 {
                     (null, false, false, 2), (member, false, true, 2), (other, false, false, 2), (liker, true, false, 2),
                     (someone, true, false, 2), (replier, false, false, 2), (blocker, false, false, 1)
                 })
        {
            var page = await Page(CommentsOf(member.UserName, "?pageSize=50"), viewer);
            Assert.Equal([toPlain, toPictured], page.Ids());

            var withPicture = Item(page, toPictured).GetProperty("parentComment");
            AssertSameComment(Item(await Page($"/api/comment/chapter/{chapter.Id}?pageSize=50", viewer), pictured), withPicture);
            Assert.Equal(pictured, withPicture.GetProperty("id").GetGuid());
            Assert.StartsWith("https://files.test/comment-images/", withPicture.GetProperty("attachedImageUrl").GetString());
            Assert.Equal(2, withPicture.GetProperty("likesCount").GetInt32());
            Assert.Equal(likesPictured, withPicture.GetProperty("isLikedByCurrentUser").GetBoolean());
            Assert.Equal("2026-09-30T08:15:42.1234567Z", withPicture.GetProperty("createdAt").GetString());
            Assert.Equal(repliesToPictured, withPicture.GetProperty("totalRepliesCount").GetInt32());

            var withoutPicture = Item(page, toPlain).GetProperty("parentComment");
            AssertSameComment(Item(await Page($"/api/comment/paragraph/{paragraph.Id}?pageSize=50", viewer), plain), withoutPicture);
            Assert.Equal(JsonValueKind.Null, withoutPicture.GetProperty("attachedImageUrl").ValueKind);
            Assert.Equal(1, withoutPicture.GetProperty("likesCount").GetInt32());
            Assert.Equal(likesPlain, withoutPicture.GetProperty("isLikedByCurrentUser").GetBoolean());
            Assert.Equal(1, withoutPicture.GetProperty("totalRepliesCount").GetInt32());
        }

        await AssertListed(member, [], [toPlain, toPictured]);
    }

    [Fact]
    public async Task A_parents_like_is_the_viewers_own_and_apart_from_the_replys()
    {
        var (member, other, liker, another) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (_, chapter, _) = await ReadableNovel(await api.SignUp());
        var thread = await api.Comment(other, OnChapter(chapter.Id), "رأي");
        var reply = await api.Comment(member, OnChapter(chapter.Id), "رد", thread);
        var mine = await api.Comment(member, OnChapter(chapter.Id), "تعليقي");
        var toMyself = await api.Comment(member, OnChapter(chapter.Id), "رد على نفسي", mine);

        // The liker and the member like the other member's comment; another member likes the reply to it, and the
        // member's own comment, which is both listed and answered by a listed reply.
        await Like(liker, thread);
        await Like(member, thread);
        await Like(another, reply);
        await Like(another, mine);

        async Task AssertLikes(ApiUser? viewer, bool parent, bool listedReply, bool myComment)
        {
            var page = await Page(CommentsOf(member.UserName), viewer);
            Assert.Equal([toMyself, mine, reply], page.Ids());
            var answering = Item(page, reply);
            Assert.Equal(listedReply, answering.GetProperty("isLikedByCurrentUser").GetBoolean());
            Assert.Equal(parent, answering.GetProperty("parentComment").GetProperty("isLikedByCurrentUser").GetBoolean());
            Assert.Equal(myComment, Item(page, mine).GetProperty("isLikedByCurrentUser").GetBoolean());
            Assert.Equal(myComment, Item(page, toMyself).GetProperty("parentComment").GetProperty("isLikedByCurrentUser").GetBoolean());
            Assert.False(Item(page, toMyself).GetProperty("isLikedByCurrentUser").GetBoolean());
            // As the chapter's comment list says to the same viewer.
            var threadList = await Page($"/api/comment/chapter/{chapter.Id}?pageSize=50", viewer);
            Assert.Equal(parent, Item(threadList, thread).GetProperty("isLikedByCurrentUser").GetBoolean());
            Assert.Equal(myComment, Item(threadList, mine).GetProperty("isLikedByCurrentUser").GetBoolean());
        }

        await AssertLikes(liker, parent: true, listedReply: false, myComment: false);
        await AssertLikes(member, parent: true, listedReply: false, myComment: false);
        await AssertLikes(another, parent: false, listedReply: true, myComment: true);
        await AssertLikes(other, parent: false, listedReply: false, myComment: false);
        await AssertLikes(null, parent: false, listedReply: false, myComment: false);

        // The liker takes their like back: the parent is no longer liked by them, and has one like fewer.
        async Task<int> ParentLikes() =>
            Item(await Page(CommentsOf(member.UserName), liker), reply).GetProperty("parentComment").GetProperty("likesCount").GetInt32();
        Assert.Equal(2, await ParentLikes());
        await Ok(await api.Send(HttpMethod.Delete, $"/api/comment/{thread}/unlike", liker));
        await AssertLikes(liker, parent: false, listedReply: false, myComment: false);
        await AssertLikes(member, parent: true, listedReply: false, myComment: false);
        Assert.Equal(1, await ParentLikes());
    }

    [Fact]
    public async Task The_parent_is_still_left_out_where_60_leaves_it_out_and_complete_everywhere_else()
    {
        var (member, other, viewer, blockedByOther, someone) =
            (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (_, chapter, _) = await ReadableNovel(await api.SignUp());
        var thread = await CommentWithPicture(other, OnChapter(chapter.Id), "رأي");
        var reply = await api.Comment(member, OnChapter(chapter.Id), "رد", thread);
        var mine = await api.Comment(member, OnChapter(chapter.Id), "تعليقي");
        await Like(viewer, thread);
        await Like(blockedByOther, thread);

        async Task AssertParent(ApiUser? someoneViewing, bool shown)
        {
            var page = await Page(CommentsOf(member.UserName), someoneViewing);
            Assert.Equal([mine, reply], page.Ids());
            Assert.Equal(2, page.GetProperty("totalItemsCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, Item(page, mine).GetProperty("parentComment").ValueKind);
            var item = Item(page, reply);
            Assert.Equal(thread, item.GetProperty("parentCommentId").GetGuid());
            var parent = item.GetProperty("parentComment");
            var threadList = await Page($"/api/comment/chapter/{chapter.Id}?pageSize=50", someoneViewing);
            if (shown)
            {
                AssertSameComment(Item(threadList, thread), parent);
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, parent.ValueKind);
                Assert.DoesNotContain(thread, threadList.Ids());
            }
        }

        // The viewer blocks the parent's author: the reply stays, without what it answers, though the viewer liked it.
        await Ok(await api.Block(viewer, other));
        await AssertParent(viewer, shown: false);
        // The parent's author blocks a member who liked it: what they wrote isn't hidden from that member.
        await Ok(await api.Block(other, blockedByOther));
        await AssertParent(blockedByOther, shown: true);
        Assert.True(Item(await Page(CommentsOf(member.UserName), blockedByOther), reply)
            .GetProperty("parentComment").GetProperty("isLikedByCurrentUser").GetBoolean());
        foreach (var anyone in new[] { null, member, other, someone })
        {
            await AssertParent(anyone, shown: true);
        }

        await Ok(await api.Unblock(viewer, other));
        await AssertParent(viewer, shown: true);
        Assert.True(Item(await Page(CommentsOf(member.UserName), viewer), reply)
            .GetProperty("parentComment").GetProperty("isLikedByCurrentUser").GetBoolean());
        await AssertListed(member, [], [mine, reply]);
    }

    [Fact]
    public async Task A_page_with_its_parents_complete_is_as_many_sql_commands_as_before_67()
    {
        var (few, many, viewer, author) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (_, chapter, _) = await ReadableNovel(author);

        // One reply, or six replies and six comments of their own, each reply to a comment by a different member,
        // with a picture and liked by the viewer, as is every second listed comment.
        async Task Write(ApiUser member, int replies, int comments)
        {
            for (var i = 0; i < replies; i++)
            {
                var thread = await CommentWithPicture(await api.SignUp(), OnChapter(chapter.Id), $"رأي {i}");
                await Like(viewer, thread);
                var reply = await api.Comment(member, OnChapter(chapter.Id), "رد", thread);
                if (i % 2 == 0)
                {
                    await Like(viewer, reply);
                }
            }
            for (var i = 0; i < comments; i++)
            {
                await api.Comment(member, OnChapter(chapter.Id), $"تعليق {i}");
            }
        }

        await Write(few, replies: 1, comments: 0);
        await Write(many, replies: 6, comments: 6);

        var log = new CommandsByRequest();
        await using var counted = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(log))));
        using var client = counted.CreateClient();

        async Task<(JsonElement Page, List<string> Commands)> Measured(ApiUser member, ApiUser? asViewer)
        {
            var marker = Guid.NewGuid().ToString("N");
            var request = new HttpRequestMessage(HttpMethod.Get, CommentsOf(member.UserName, "?pageSize=50"));
            request.Headers.Add(CommandsByRequest.Header, marker);
            if (asViewer != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", asViewer.Token);
            }
            var page = await (await client.SendAsync(request)).OkJson();
            return (page, log.Of(marker));
        }

        foreach (var asViewer in new[] { null, viewer })
        {
            foreach (var (member, items, replies) in new[] { (few, 1, 1), (many, 12, 6) })
            {
                // Signed in, the token's state is read once and then cached for a minute: read it just before.
                await Measured(member, asViewer);
                var (page, commands) = await Measured(member, asViewer);

                Assert.Equal(items, page.Ids().Count);
                var parents = page.GetProperty("items").EnumerateArray()
                    .Where(i => i.GetProperty("isReply").GetBoolean())
                    .Select(i => i.GetProperty("parentComment"))
                    .ToList();
                Assert.Equal(replies, parents.Count);
                Assert.All(parents, parent =>
                {
                    Assert.Equal(ParentFields, Names(parent));
                    Assert.StartsWith("https://files.test/comment-images/", parent.GetProperty("attachedImageUrl").GetString());
                    Assert.Equal(1, parent.GetProperty("likesCount").GetInt32());
                    Assert.Equal(asViewer != null, parent.GetProperty("isLikedByCurrentUser").GetBoolean());
                    Assert.Equal(1, parent.GetProperty("totalRepliesCount").GetInt32());
                });

                // As before #67, for one item or twelve: the member, the total and the page, which reads the parents
                // whole (their replies counted in it); signed in, also the block check and one read of the viewer's
                // likes, the parents' with the items'. Comments on the chapter itself need no early-access check.
                Assert.Equal(asViewer == null ? 3 : 5, commands.Count);
                Assert.Equal(asViewer == null ? 0 : 1, commands.Count(sql => sql.Contains("[CommentLikes]")));
            }
        }
    }
}
