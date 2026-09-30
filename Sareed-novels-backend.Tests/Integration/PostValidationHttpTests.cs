using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.Posts;
using Application.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Post = Domain.Entities.Post;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Writing a post (#43): POST /api/posts with multipart form-data as the apps send it, through the real model binding.
/// Text, a picture and a novel, at least one of them; at most 5000 characters as people count them; a JPEG, PNG or
/// WebP picture of at most 5 MB. A refusal is 400 <c>{ success: false, code, message }</c> with the code of the first
/// rule broken, and a picture that can't be stored is UploadFailed and leaves no post.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class PostValidationHttpTests(SardApiFactory api)
{
    private const int FiveMegabytes = 5 * 1024 * 1024;
    private const int SixMegabytes = 6 * 1024 * 1024;

    // One character each, in 2, 11, 2 and 3 UTF-16 units: an emoji, a family (four people joined by ZWJ), a letter with
    // a fatha, and one with a shadda and a fatha.
    private const string Smile = "\U0001F600";
    private const string Family = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466";
    private const string WithFatha = "بَ";
    private const string WithShadda = "دَّ";

    private static string Repeat(string text, int times) => string.Concat(Enumerable.Repeat(text, times));

    private Task<HttpResponseMessage> Post(ApiUser member, HttpContent form) =>
        api.Send(HttpMethod.Post, "/api/posts", member, form);

    /// <summary>
    /// A form without any field, as a browser sends an empty FormData: only the closing boundary. (.NET's empty
    /// MultipartFormDataContent has one part without headers, which ASP.NET refuses as unreadable before any rule.)
    /// </summary>
    private static HttpContent EmptyForm()
    {
        var content = new StringContent("--nothing--\r\n");
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=nothing");
        return content;
    }

    /// <summary>
    /// The form with a file as Image, of this size and declared type (null: no Content-Type). Its bytes are zeros: the
    /// API checks the type the client declares, not the bytes.
    /// </summary>
    private static MultipartFormDataContent WithImage(MultipartFormDataContent form, int bytes = 4, string? contentType = "image/png")
    {
        var image = new ByteArrayContent(new byte[bytes]);
        if (contentType is not null)
        {
            image.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }
        form.Add(image, "Image", "picture");
        return form;
    }

    private static async Task<JsonElement> Published(HttpResponseMessage response)
    {
        var body = await response.OkJson();
        Assert.True(body.GetProperty("success").GetBoolean());
        return body.GetProperty("post");
    }

    /// <summary>Checks a refusal: 400, the endpoint's shape, this code and this Arabic message.</summary>
    private static async Task Refused(HttpResponseMessage response, string code, string message)
    {
        var body = await response.Error(HttpStatusCode.BadRequest);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(message, body.GetProperty("message").GetString());
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("post").ValueKind);
    }

    private async Task<List<Post>> PostsBy(ApiUser member)
    {
        await using var db = api.Db();
        return await db.Posts.AsNoTracking().Where(p => p.UserId == member.Id).ToListAsync();
    }

    // ─── Text ───

    public static TheoryData<string?> NoText => new() { null, "", "   ", "\r\n\t", "  　" };

    [Theory]
    [MemberData(nameof(NoText))]
    public async Task No_text_and_nothing_attached_is_PostContentRequired(string? content)
    {
        var member = await api.SignUp();
        var form = content is null ? EmptyForm() : ReaderApi.Form(("Content", content));

        await Refused(await Post(member, form), PostRules.ContentRequiredCode, PostRules.ContentRequiredMessage);

        Assert.Empty(await PostsBy(member));
    }

    [Fact]
    public async Task A_post_with_only_a_picture_or_only_a_novel_is_published_without_text()
    {
        var member = await api.SignUp();
        var novel = await api.AddNovel(member);

        var picture = await Published(await Post(member, WithImage(ReaderApi.Form())));
        var pictureAndBlank = await Published(await Post(member, WithImage(ReaderApi.Form(("Content", "  \n ")))));
        var novelOnly = await Published(await Post(member, ReaderApi.Form(("NovelId", novel.Id.ToString()))));

        // No text is "" (the column can't be null), in the answer and wherever the post is read.
        Assert.All(new[] { picture, pictureAndBlank, novelOnly }, post => Assert.Equal("", post.GetProperty("content").GetString()));
        Assert.StartsWith("https://files.test/post-images/", picture.GetProperty("imageUrl").GetString());
        Assert.StartsWith("https://files.test/post-images/", pictureAndBlank.GetProperty("imageUrl").GetString());
        Assert.Equal(JsonValueKind.Null, novelOnly.GetProperty("imageUrl").ValueKind);
        Assert.Equal(novel.Id, novelOnly.GetProperty("novel").GetProperty("id").GetGuid());
        var stored = await PostsBy(member);
        Assert.Equal(3, stored.Count);
        Assert.All(stored, post => Assert.Equal("", post.Content));
        var listed = await (await api.Get($"/api/posts/user/{member.Id}", member)).OkJson();
        Assert.All(listed.GetProperty("items").EnumerateArray(), post => Assert.Equal("", post.GetProperty("content").GetString()));
    }

    [Fact]
    public async Task The_text_is_stored_trimmed()
    {
        var member = await api.SignUp();

        var post = await Published(await Post(member, ReaderApi.Form(("Content", "\n  سطر أول\nسطر ثانٍ  \t\r\n"))));

        Assert.Equal("سطر أول\nسطر ثانٍ", post.GetProperty("content").GetString());
        Assert.Equal("سطر أول\nسطر ثانٍ", Assert.Single(await PostsBy(member)).Content);
    }

    public static TheoryData<string> OneCharacter => new() { "ب", Smile, Family, WithFatha, WithShadda };

    [Theory]
    [MemberData(nameof(OneCharacter))]
    public async Task Five_thousand_characters_are_published_and_one_more_is_PostContentTooLong(string character)
    {
        var member = await api.SignUp();
        var atLimit = Repeat(character, PostRules.ContentMaxLength);

        // Counted after trimming, in characters, whatever their UTF-16 length (up to 55,000 units here).
        var post = await Published(await Post(member, ReaderApi.Form(("Content", "  " + atLimit + "\n"))));
        Assert.Equal(atLimit, post.GetProperty("content").GetString());
        Assert.Equal(atLimit, Assert.Single(await PostsBy(member)).Content);

        await Refused(await Post(member, ReaderApi.Form(("Content", atLimit + character))),
            PostRules.ContentTooLongCode, PostRules.ContentTooLongMessage);
        Assert.Single(await PostsBy(member));
    }

    [Fact]
    public async Task Text_within_the_characters_but_over_100000_UTF16_units_is_PostContentTooLong()
    {
        var member = await api.SignUp();
        // One letter under 99,999 fathas is one character in 100,000 units: the most a post's text may take.
        var atLimit = "ب" + Repeat("َ", PostRules.ContentMaxUtf16Length - 1);

        await Published(await Post(member, ReaderApi.Form(("Content", atLimit))));
        await Refused(await Post(member, ReaderApi.Form(("Content", atLimit + "َ"))),
            PostRules.ContentTooLongCode, PostRules.ContentTooLongMessage);

        Assert.Equal(PostRules.ContentMaxUtf16Length, Assert.Single(await PostsBy(member)).Content.Length);
    }

    // ─── Picture ───

    [Fact]
    public async Task A_picture_of_5_MB_is_published_and_a_byte_more_is_PostImageTooLarge()
    {
        var member = await api.SignUp();

        var post = await Published(await Post(member, WithImage(ReaderApi.Form(("Content", "صورة")), FiveMegabytes)));
        Assert.StartsWith("https://files.test/post-images/", post.GetProperty("imageUrl").GetString());

        foreach (var bytes in new[] { FiveMegabytes + 1, SixMegabytes })
        {
            await Refused(await Post(member, WithImage(ReaderApi.Form(("Content", "صورة")), bytes)),
                PostRules.ImageTooLargeCode, PostRules.ImageTooLargeMessage);
        }
        Assert.Single(await PostsBy(member));
    }

    [Fact]
    public async Task A_JPEG_PNG_or_WebP_picture_is_published_and_any_other_is_PostImageType()
    {
        var member = await api.SignUp();

        foreach (var type in new[] { "image/jpeg", "image/png", "image/webp", "image/jpg" })
        {
            var post = await Published(await Post(member, WithImage(ReaderApi.Form(("Content", type)), contentType: type)));
            Assert.StartsWith("https://files.test/post-images/", post.GetProperty("imageUrl").GetString());
        }

        foreach (var type in new[] { "image/gif", "image/heic", "image/heif", "image/avif", "image/svg+xml", "application/octet-stream", null })
        {
            await Refused(await Post(member, WithImage(ReaderApi.Form(("Content", "صورة")), contentType: type)),
                PostRules.ImageTypeCode, PostRules.ImageTypeMessage);
        }
        // An empty file is no picture, whatever type it declares.
        await Refused(await Post(member, WithImage(ReaderApi.Form(("Content", "صورة")), bytes: 0)),
            PostRules.ImageTypeCode, PostRules.ImageTypeMessage);

        Assert.Equal(4, (await PostsBy(member)).Count);
    }

    // ─── Which answer ───

    [Fact]
    public async Task A_refusal_carries_one_code_the_first_rule_broken()
    {
        var member = await api.SignUp();
        var tooLong = Repeat("ب", PostRules.ContentMaxLength + 1);
        var unknownNovel = Guid.NewGuid().ToString();

        // The text before the picture.
        await Refused(await Post(member, WithImage(ReaderApi.Form(("Content", tooLong)), contentType: "image/gif")),
            PostRules.ContentTooLongCode, PostRules.ContentTooLongMessage);
        // A picture makes text optional, even one refused afterwards.
        await Refused(await Post(member, WithImage(ReaderApi.Form(), contentType: "image/heic")),
            PostRules.ImageTypeCode, PostRules.ImageTypeMessage);
        // The picture's type before its size.
        await Refused(await Post(member, WithImage(ReaderApi.Form(("Content", "صورة")), SixMegabytes, "image/gif")),
            PostRules.ImageTypeCode, PostRules.ImageTypeMessage);
        // The rules before the novel.
        await Refused(await Post(member, ReaderApi.Form(("Content", tooLong), ("NovelId", unknownNovel))),
            PostRules.ContentTooLongCode, PostRules.ContentTooLongMessage);
        await Refused(await Post(member, WithImage(ReaderApi.Form(("NovelId", unknownNovel)), SixMegabytes)),
            PostRules.ImageTooLargeCode, PostRules.ImageTooLargeMessage);

        Assert.Empty(await PostsBy(member));
    }

    [Fact]
    public async Task A_novel_that_does_not_exist_is_NovelNotFound_as_before_with_or_without_text()
    {
        var member = await api.SignUp();
        var unknownNovel = Guid.NewGuid().ToString();

        await Refused(await Post(member, ReaderApi.Form(("Content", "منشور"), ("NovelId", unknownNovel))), "NovelNotFound", "الرواية غير موجودة");
        await Refused(await Post(member, ReaderApi.Form(("NovelId", unknownNovel))), "NovelNotFound", "الرواية غير موجودة");

        Assert.Empty(await PostsBy(member));
    }

    [Fact]
    public async Task Signed_out_it_is_401_before_any_rule()
    {
        var empty = await api.Send(HttpMethod.Post, "/api/posts", null, EmptyForm());
        var gif = await api.Send(HttpMethod.Post, "/api/posts", null, WithImage(ReaderApi.Form(), SixMegabytes, "image/gif"));

        Assert.Equal(HttpStatusCode.Unauthorized, empty.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, gif.StatusCode);
    }

    [Fact]
    public async Task The_refusals_say_what_is_wrong_in_arabic()
    {
        // What the apps show when they show the server's message (the codes are what they branch on).
        Assert.Equal("المنشور فارغ: اكتب نصًا أو أرفق صورة أو رواية.", PostRules.ContentRequiredMessage);
        Assert.Equal("المنشور طويل: الحد الأقصى 5000 حرف.", PostRules.ContentTooLongMessage);
        Assert.Equal("صيغة الصورة غير مدعومة: اختر صورة JPEG أو PNG أو WebP.", PostRules.ImageTypeMessage);
        Assert.Equal("الصورة كبيرة: الحد الأقصى 5 ميغابايت.", PostRules.ImageTooLargeMessage);
        Assert.Equal("تعذّر رفع الصورة، حاول مرة أخرى.", PostRules.UploadFailedMessage);

        // Each rule's own code, from the validator itself: a refusal carries the specific code, not ValidationFailed.
        var member = await api.SignUp();
        await Refused(await Post(member, EmptyForm()), "PostContentRequired", PostRules.ContentRequiredMessage);
        await Refused(await Post(member, ReaderApi.Form(("Content", Repeat("ب", 5001)))), "PostContentTooLong", PostRules.ContentTooLongMessage);
        await Refused(await Post(member, WithImage(ReaderApi.Form(), contentType: "image/gif")), "PostImageType", PostRules.ImageTypeMessage);
        await Refused(await Post(member, WithImage(ReaderApi.Form(), SixMegabytes)), "PostImageTooLarge", PostRules.ImageTooLargeMessage);
    }

    // ─── Storing the picture ───

    [Fact]
    public async Task A_picture_that_cannot_be_stored_is_UploadFailed_and_no_post_is_created()
    {
        var member = await api.SignUp();
        var storage = new FailingPostUploads();
        await using var storageDown = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileUploadService>();
            services.AddSingleton<IFileUploadService>(storage);
        }));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/posts") { Content = WithImage(ReaderApi.Form(("Content", "منشور بصورة"))) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", member.Token);
        var response = await storageDown.CreateClient().SendAsync(request);

        await Refused(response, "UploadFailed", "تعذّر رفع الصورة، حاول مرة أخرى.");
        Assert.Equal(1, storage.Attempts);
        Assert.Empty(await PostsBy(member));
    }

    /// <summary>Storage that is down for post pictures (the only upload these requests make).</summary>
    private sealed class FailingPostUploads : IFileUploadService
    {
        public int Attempts;

        public Task<string> UploadPostImageAsync(Stream fileStream, string contentType, string postId)
        {
            Interlocked.Increment(ref Attempts);
            return Task.FromException<string>(new HttpRequestException("R2 is down"));
        }

        public Task<string> UploadImageAsync(Stream fileStream, string fileName, string contentType, string userId) => throw new NotSupportedException();
        public Task<string> UploadProfileBannerAsync(Stream fileStream, string contentType, string userId) => throw new NotSupportedException();
        public Task<bool> DeleteImageAsync(string keyOrUrl) => throw new NotSupportedException();
        public Task<string> UploadNovelImageAsync(Stream fileStream, string contentType, string novelId) => throw new NotSupportedException();
        public Task<string> UploadCharacterImageAsync(Stream fileStream, string contentType, string characterId) => throw new NotSupportedException();
        public Task<string> UploadCommentImageAsync(Stream fileStream, string contentType, string commentId) => throw new NotSupportedException();
        public Task<string> UploadReadingListCoverImageAsync(Stream fileStream, string contentType, string readingListId) => throw new NotSupportedException();
        public Task<string> UploadEntityGalleryImageAsync(Stream fileStream, string contentType, string entityId) => throw new NotSupportedException();
        public Task<string> UploadPaymentProofAsync(Stream fileStream, string contentType, string userId) => throw new NotSupportedException();
        public Task<string> UploadGiftImageAsync(Stream fileStream, string contentType, string giftId) => throw new NotSupportedException();
    }
}
