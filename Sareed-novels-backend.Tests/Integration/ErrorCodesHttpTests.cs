using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The answers clients branch on carry a stable code (#17): the idempotency answers the mobile app matched by their
/// English text (already following, already liked...), and the refusals the web told apart by words in the message
/// (already in the list, not published, a missing paragraph rather than a missing parent comment).
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ErrorCodesHttpTests(SardApiFactory api)
{
    /// <summary>The code of a refused request with this status (the body's message must be there too).</summary>
    private static async Task<string> Code(HttpResponseMessage response, HttpStatusCode status)
    {
        var body = await response.Error(status);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()), body.GetRawText());
        return body.GetProperty("code").GetString()!;
    }

    private Task<HttpResponseMessage> Post(string url, ApiUser user, HttpContent? content = null) => api.Send(HttpMethod.Post, url, user, content);
    private Task<HttpResponseMessage> Delete(string url, ApiUser user, HttpContent? content = null) => api.Send(HttpMethod.Delete, url, user, content);

    [Fact]
    public async Task Following_a_user_twice_unfollowing_nobody_and_following_oneself()
    {
        var (me, them) = (await api.SignUp(), await api.SignUp());

        (await api.Follow(me, them)).EnsureSuccessStatusCode();
        Assert.Equal("AlreadyFollowing", await Code(await api.Follow(me, them), HttpStatusCode.BadRequest));
        Assert.Equal("CannotFollowSelf", await Code(await api.Follow(me, me), HttpStatusCode.BadRequest));

        var unfollow = () => Delete("/api/User/unfollow", me, JsonContent.Create(new { userToUnFollowId = them.Id }));
        (await unfollow()).EnsureSuccessStatusCode();
        Assert.Equal("NotFollowing", await Code(await unfollow(), HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Liking_a_post_twice_and_unliking_one_not_liked()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);

        (await Post($"/api/posts/{post}/like", reader)).EnsureSuccessStatusCode();
        Assert.Equal("AlreadyLiked", await Code(await Post($"/api/posts/{post}/like", reader), HttpStatusCode.BadRequest));
        (await Delete($"/api/posts/{post}/unlike", reader)).EnsureSuccessStatusCode();
        Assert.Equal("NotLiked", await Code(await Delete($"/api/posts/{post}/unlike", reader), HttpStatusCode.BadRequest));
        Assert.Equal("PostNotFound", await Code(await Post($"/api/posts/{Guid.NewGuid()}/like", reader), HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Liking_a_comment_twice_ones_own_and_unliking_one_not_liked()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var post = await api.Post(author);
        var comment = await api.Comment(author, $"/api/comment/post/{post}");

        (await Post($"/api/comment/{comment}/like", reader)).EnsureSuccessStatusCode();
        Assert.Equal("AlreadyLiked", await Code(await Post($"/api/comment/{comment}/like", reader), HttpStatusCode.BadRequest));
        Assert.Equal("CannotLikeOwnContent", await Code(await Post($"/api/comment/{comment}/like", author), HttpStatusCode.BadRequest));
        (await Delete($"/api/comment/{comment}/unlike", reader)).EnsureSuccessStatusCode();
        Assert.Equal("NotLiked", await Code(await Delete($"/api/comment/{comment}/unlike", reader), HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Reviewing_twice_ones_own_novel_and_liking_a_review_twice()
    {
        var (author, reader, other) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var review = await api.Review(reader, novel.Id);
        var again = () => Post($"/api/{novel.Id}", reader, JsonContent.Create(new
        {
            writingQualityScore = 4, updatingStabilityScore = 4, characterDevelopmentScore = 4, worldBuildingScore = 4, content = "رواية جميلة جداً"
        }));

        Assert.Equal("AlreadyReviewed", await Code(await again(), HttpStatusCode.BadRequest));
        Assert.Equal("CannotReviewOwnNovel", await Code(await Post($"/api/{novel.Id}", author, JsonContent.Create(new
        {
            writingQualityScore = 4, updatingStabilityScore = 4, characterDevelopmentScore = 4, worldBuildingScore = 4
        })), HttpStatusCode.BadRequest));

        (await Post($"/api/{novel.Id}/reviews/{review}/like", other)).EnsureSuccessStatusCode();
        Assert.Equal("AlreadyLiked", await Code(await Post($"/api/{novel.Id}/reviews/{review}/like", other), HttpStatusCode.BadRequest));
        Assert.Equal("CannotLikeOwnContent", await Code(await Post($"/api/{novel.Id}/reviews/{review}/like", reader), HttpStatusCode.BadRequest));
        (await Delete($"/api/{novel.Id}/reviews/{review}/unlike", other)).EnsureSuccessStatusCode();
        Assert.Equal("NotLiked", await Code(await Delete($"/api/{novel.Id}/reviews/{review}/unlike", other), HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Reading_list_refusals_the_web_tells_apart()
    {
        var (owner, other) = (await api.SignUp(), await api.SignUp());
        var list = await api.ReadingList(owner, name: "مفضلتي");
        var novel = await api.AddNovel(other);
        var draft = await api.AddNovel(other, isDraft: true);

        // Same name as an existing list: the web says "you already have a list with this name".
        string name;
        await using (var db = api.Db())
        {
            name = db.ReadingLists.Single(l => l.Id == list).Name;
        }
        var duplicate = await Post("/api/readinglist", owner, ReaderApi.Form(("Name", name), ("IsPublic", "true")));
        Assert.Equal("DuplicateListName", await Code(duplicate, HttpStatusCode.BadRequest));

        (await Post($"/api/readinglist/{list}/novels/{novel.Id}", owner)).EnsureSuccessStatusCode();
        Assert.Equal("AlreadyInList", await Code(await Post($"/api/readinglist/{list}/novels/{novel.Id}", owner), HttpStatusCode.BadRequest));
        Assert.Equal("NovelNotPublished", await Code(await Post($"/api/readinglist/{list}/novels/{draft.Id}", owner), HttpStatusCode.BadRequest));
        (await Delete($"/api/readinglist/{list}/novels/{novel.Id}", owner)).EnsureSuccessStatusCode();
        Assert.Equal("NotInList", await Code(await Delete($"/api/readinglist/{list}/novels/{novel.Id}", owner), HttpStatusCode.BadRequest));

        Assert.Equal("CannotFollowOwnList", await Code(await Post($"/api/readinglist/{list}/follow", owner), HttpStatusCode.BadRequest));
        (await Post($"/api/readinglist/{list}/follow", other)).EnsureSuccessStatusCode();
        Assert.Equal("AlreadyFollowing", await Code(await Post($"/api/readinglist/{list}/follow", other), HttpStatusCode.BadRequest));
        (await Delete($"/api/readinglist/{list}/unfollow", other)).EnsureSuccessStatusCode();
        Assert.Equal("NotFollowing", await Code(await Delete($"/api/readinglist/{list}/unfollow", other), HttpStatusCode.BadRequest));
        Assert.Equal("NotOwner", await Code(await Post($"/api/readinglist/{list}/novels/{novel.Id}", other), HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task A_paragraph_comment_tells_a_missing_paragraph_from_a_missing_parent()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var (_, paragraphs) = await api.AddChapter(novel, "<p>الفقرة الأولى</p>");

        var noParagraph = await Post($"/api/comment/paragraph/{Guid.NewGuid()}", author, ReaderApi.Form(("Content", "تعليق")));
        Assert.Equal("ParagraphNotFound", await Code(noParagraph, HttpStatusCode.NotFound));

        var noParent = await Post($"/api/comment/paragraph/{paragraphs[0].Id}", author,
            ReaderApi.Form(("Content", "رد"), ("ParentCommentId", Guid.NewGuid().ToString())));
        Assert.Equal("ParentCommentNotFound", await Code(noParent, HttpStatusCode.NotFound));

        // A reply to a reply: JSON with a code now, not a plain-text sentence.
        var top = await api.Comment(author, $"/api/comment/paragraph/{paragraphs[0].Id}");
        var reply = await api.Comment(author, $"/api/comment/paragraph/{paragraphs[0].Id}", parentId: top);
        var nested = await Post($"/api/comment/paragraph/{paragraphs[0].Id}", author,
            ReaderApi.Form(("Content", "رد على رد"), ("ParentCommentId", reply.ToString())));
        Assert.Equal("NestedReplyNotAllowed", await Code(nested, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Points_refusals_have_codes()
    {
        var (author, reader) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var rose = Guid.Parse("ec16dfde-71b8-4e23-8ff5-d1846cdf2036");
        await using (var db = api.Db())
        {
            db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = reader.Id, CurrentBalance = 150 });
            db.NovelPrivileges.Add(new NovelPrivilege
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, IsEnabled = true, SubscriptionCost = 100, CurrentLockedCount = 5, PrivilegeStartSequence = 11
            });
            await db.SaveChangesAsync();
        }
        var gift = (ApiUser sender, int count) => Post("/api/gift/send", sender, JsonContent.Create(new { giftId = rose, novelId = novel.Id, count }));

        Assert.Equal("CannotGiftOwnNovel", await Code(await gift(author, 1), HttpStatusCode.BadRequest));
        Assert.Equal("InvalidGiftCount", await Code(await gift(reader, 0), HttpStatusCode.BadRequest));
        Assert.Equal("InsufficientBalance", await Code(await gift(reader, 2), HttpStatusCode.BadRequest));

        (await Post($"/api/novel/{novel.Id}/privilege/subscribe", reader)).EnsureSuccessStatusCode();
        Assert.Equal("AlreadySubscribed", await Code(await Post($"/api/novel/{novel.Id}/privilege/subscribe", reader), HttpStatusCode.BadRequest));
        Assert.Equal("CannotSubscribeToOwnNovel", await Code(await Post($"/api/novel/{novel.Id}/privilege/subscribe", author), HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Sign_in_and_password_refusals_have_codes()
    {
        var user = await api.SignUp();

        var wrong = await api.Client().PostAsJsonAsync("/api/identity/Login", new { loginCardinality = user.UserName, password = "not-the-password" });
        Assert.Equal("InvalidCredentials", await Code(wrong, HttpStatusCode.Forbidden));

        var change = await api.Send(HttpMethod.Patch, "/api/User/update-password", user,
            JsonContent.Create(new { currentPassword = "not-the-password", newPassword = "Another-horse-2" }));
        var body = await change.Error(HttpStatusCode.BadRequest);
        Assert.Equal("PasswordMismatch", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.Equal("PasswordMismatch", body.GetProperty("errors")[0].GetProperty("code").GetString());
    }
}
