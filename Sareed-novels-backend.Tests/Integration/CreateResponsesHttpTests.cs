using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Creating a comment, review, post or reading list returns the new item exactly as the matching list (or page)
/// returns it, next to the success and message the web app reads, so the apps can insert it without a refetch.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class CreateResponsesHttpTests(SardApiFactory api)
{
    private static void AssertSameJson(JsonElement expected, JsonElement actual) =>
        Assert.Equal(expected.GetRawText(), actual.GetRawText());

    private static JsonElement FirstItem(JsonElement page, string list = "items") =>
        page.GetProperty(list).EnumerateArray().First();

    private static async Task<JsonElement> Created(HttpResponseMessage response, string property)
    {
        var body = await response.OkJson();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("message").GetString()));
        return body.GetProperty(property);
    }

    private static MultipartFormDataContent WithImage(MultipartFormDataContent form, string field)
    {
        var image = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, field, "photo.png");
        return form;
    }

    [Fact]
    public async Task A_new_chapter_comment_comes_back_as_the_chapter_list_shows_it()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var (chapter, _) = await api.AddChapter(await api.AddNovel(author), "<p>فقرة</p>");

        var response = await api.Send(HttpMethod.Post, $"/api/comment/chapter/{chapter.Id}", reader,
            WithImage(ReaderApi.Form(("Content", "تعليق على الفصل")), "AttachedImage"));
        var comment = await Created(response, "comment");

        Assert.Equal("تعليق على الفصل", comment.GetProperty("content").GetString());
        Assert.Equal(reader.Id, comment.GetProperty("user").GetProperty("id").GetString());
        Assert.StartsWith("https://files.test/comment-images/", comment.GetProperty("attachedImageUrl").GetString());
        Assert.Equal(0, comment.GetProperty("likesCount").GetInt32());
        Assert.False(comment.GetProperty("isLikedByCurrentUser").GetBoolean());
        Assert.Equal(JsonValueKind.Null, comment.GetProperty("parentCommentId").ValueKind);
        var listed = await (await api.Get($"/api/comment/chapter/{chapter.Id}", reader)).OkJson();
        AssertSameJson(FirstItem(listed), comment);
    }

    [Fact]
    public async Task A_new_paragraph_comment_comes_back_as_the_paragraph_list_shows_it()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var (_, paragraphs) = await api.AddChapter(await api.AddNovel(author), "<p>الأولى</p>", "<p>الثانية</p>");

        var response = await api.Send(HttpMethod.Post, $"/api/comment/paragraph/{paragraphs[1].Id}", reader,
            ReaderApi.Form(("Content", "تعليق على الفقرة")));
        var comment = await Created(response, "comment");

        var listed = await (await api.Get($"/api/comment/paragraph/{paragraphs[1].Id}", reader)).OkJson();
        AssertSameJson(FirstItem(listed), comment);
    }

    [Fact]
    public async Task A_new_post_comment_comes_back_as_the_post_list_shows_it()
    {
        var poster = await api.SignUp();
        var reader = await api.SignUp();
        var post = await Created(await api.Send(HttpMethod.Post, "/api/posts", poster, ReaderApi.Form(("Content", "منشور"))), "post");
        var postId = post.GetProperty("id").GetGuid();

        var comment = await Created(
            await api.Send(HttpMethod.Post, $"/api/comment/post/{postId}", reader, ReaderApi.Form(("Content", "تعليق على المنشور"))),
            "comment");

        var listed = await (await api.Get($"/api/comment/post/{postId}", reader)).OkJson();
        AssertSameJson(FirstItem(listed), comment);
    }

    [Fact]
    public async Task A_new_reply_comes_back_with_its_parent_as_the_replies_list_shows_it()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var (_, paragraphs) = await api.AddChapter(await api.AddNovel(author), "<p>فقرة</p>");
        var url = $"/api/comment/paragraph/{paragraphs[0].Id}";
        var parent = await Created(await api.Send(HttpMethod.Post, url, author, ReaderApi.Form(("Content", "تعليق"))), "comment");
        var parentId = parent.GetProperty("id").GetString()!;

        var reply = await Created(
            await api.Send(HttpMethod.Post, url, reader, ReaderApi.Form(("Content", "رد"), ("ParentCommentId", parentId))),
            "comment");

        Assert.Equal(parentId, reply.GetProperty("parentCommentId").GetString());
        var listed = FirstItem(await (await api.Get($"/api/comment/chapter/comments/{parentId}", reader)).OkJson());
        foreach (var field in listed.EnumerateObject())
        {
            AssertSameJson(field.Value, reply.GetProperty(field.Name));
        }
        // The parent's list now counts the reply, as it would after a refetch.
        var parentListed = FirstItem(await (await api.Get(url, reader)).OkJson());
        Assert.Equal(1, parentListed.GetProperty("totalRepliesCount").GetInt32());
    }

    [Fact]
    public async Task A_refused_comment_answers_as_before()
    {
        var author = await api.SignUp();
        var (chapter, _) = await api.AddChapter(await api.AddNovel(author), "<p>فقرة</p>");
        var url = $"/api/comment/chapter/{chapter.Id}";
        var parent = await Created(await api.Send(HttpMethod.Post, url, author, ReaderApi.Form(("Content", "تعليق"))), "comment");
        var reply = await Created(await api.Send(HttpMethod.Post, url, author,
            ReaderApi.Form(("Content", "رد"), ("ParentCommentId", parent.GetProperty("id").GetString()!))), "comment");

        var nested = await api.Send(HttpMethod.Post, url, author,
            ReaderApi.Form(("Content", "رد على رد"), ("ParentCommentId", reply.GetProperty("id").GetString()!)));

        var refused = await nested.Error(HttpStatusCode.BadRequest);
        Assert.Equal("NestedReplyNotAllowed", refused.GetProperty("code").GetString());
        Assert.False(refused.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task A_new_review_comes_back_as_the_review_list_shows_it()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);
        var scores = new
        {
            writingQualityScore = 4, updatingStabilityScore = 5, characterDevelopmentScore = 3, worldBuildingScore = 4,
            isSpoiler = false, content = "رواية جميلة جداً"
        };

        var review = await Created(await api.Send(HttpMethod.Post, $"/api/{novel.Id}", reader, JsonContent.Create(scores)), "review");

        Assert.Equal(reader.UserName, review.GetProperty("reviewer").GetProperty("userName").GetString());
        Assert.Equal(4m, review.GetProperty("totalAverageScore").GetDecimal());
        var listed = await (await api.Get($"/api/{novel.Id}?sorting=newest", reader)).OkJson();
        AssertSameJson(FirstItem(listed, "reviews"), review);

        var again = await api.Send(HttpMethod.Post, $"/api/{novel.Id}", reader, JsonContent.Create(scores));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var refused = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(refused.GetProperty("success").GetBoolean());
        Assert.Equal("You have already reviewed this novel", refused.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_new_post_comes_back_as_the_post_page_and_the_user_list_show_it()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);

        var post = await Created(await api.Send(HttpMethod.Post, "/api/posts", author,
            WithImage(ReaderApi.Form(("Content", "فصل جديد قريباً"), ("NovelId", novel.Id.ToString())), "Image")), "post");

        Assert.Equal(novel.Id, post.GetProperty("novel").GetProperty("id").GetGuid());
        Assert.StartsWith("https://files.test/post-images/", post.GetProperty("imageUrl").GetString());
        var page = await (await api.Get($"/api/posts/{post.GetProperty("id").GetGuid()}", author)).OkJson();
        AssertSameJson(page, post);
        var listed = await (await api.Get($"/api/posts/user/{author.Id}", author)).OkJson();
        AssertSameJson(FirstItem(listed), post);
    }

    [Fact]
    public async Task A_new_reading_list_comes_back_as_my_lists_shows_it()
    {
        var reader = await api.SignUp();

        var list = await Created(await api.Send(HttpMethod.Post, "/api/readinglist", reader,
            ReaderApi.Form(("Name", "للقراءة لاحقاً"), ("Description", "قائمتي"), ("IsPublic", "true"))), "readingList");

        Assert.Equal("للقراءة لاحقاً", list.GetProperty("name").GetString());
        Assert.Equal(0, list.GetProperty("novelsCount").GetInt32());
        var mine = await (await api.Get("/api/readinglist/my-lists", reader)).OkJson();
        AssertSameJson(FirstItem(mine), list);
    }

    [Fact]
    public async Task A_reading_list_can_be_created_with_its_first_novel()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);

        var list = await Created(await api.Send(HttpMethod.Post, "/api/readinglist", reader,
            ReaderApi.Form(("Name", "المفضلة"), ("NovelId", novel.Id.ToString()))), "readingList");

        Assert.Equal(1, list.GetProperty("novelsCount").GetInt32());
        var preview = Assert.Single(list.GetProperty("previewNovels").EnumerateArray());
        Assert.Equal(novel.Id, preview.GetProperty("novelId").GetGuid());
        var mine = await (await api.Get("/api/readinglist/my-lists", reader)).OkJson();
        AssertSameJson(FirstItem(mine), list);
        var detail = await (await api.Get($"/api/readinglist/{list.GetProperty("id").GetGuid()}", reader)).OkJson();
        Assert.Equal(novel.Id, Assert.Single(detail.GetProperty("novels").EnumerateArray()).GetProperty("novelId").GetGuid());

        // Adding it again goes through the usual duplicate check.
        var again = await api.Send(HttpMethod.Post, $"/api/readinglist/{list.GetProperty("id").GetGuid()}/novels/{novel.Id}", reader);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task No_reading_list_is_created_with_a_novel_that_cannot_be_added()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var draft = await api.AddNovel(author, isDraft: true);

        var withDraft = await api.Send(HttpMethod.Post, "/api/readinglist", reader,
            ReaderApi.Form(("Name", "قائمة"), ("NovelId", draft.Id.ToString())));
        var withUnknown = await api.Send(HttpMethod.Post, "/api/readinglist", reader,
            ReaderApi.Form(("Name", "قائمة"), ("NovelId", Guid.NewGuid().ToString())));

        Assert.Equal(HttpStatusCode.BadRequest, withDraft.StatusCode);
        var refused = await withDraft.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(refused.GetProperty("success").GetBoolean());
        Assert.Equal("Cannot add deleted or draft novels to reading list", refused.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, withUnknown.StatusCode);
        var mine = await (await api.Get("/api/readinglist/my-lists", reader)).OkJson();
        Assert.Equal(0, mine.GetProperty("totalItemsCount").GetInt32());
    }
}
