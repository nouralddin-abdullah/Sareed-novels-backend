using System.Net;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #52: a single post, its discussion, likes, comments on posts, replies and notifications respect a block, as the
/// lists already did. To a member the author blocked, a post and its discussion are unavailable (404 PostUnavailable),
/// while a member who blocked the author still opens the post, flagged; neither can like, comment on or reply to the
/// other's content (403 Blocked), and no notification passes between them.
/// </summary>
public partial class BlockHttpTests
{
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
}
