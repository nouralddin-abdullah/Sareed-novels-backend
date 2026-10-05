using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Application.Chapters;
using Application.Chapters.Paragraphs;
using Application.Covers;
using Application.Services;
using Domain.Constants;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sareed_novels_backend.Extensions;
using Sareed_novels_backend.Middlewares;
using SkiaSharp;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Chapter pictures (#86): POST /api/novel/{novelId}/chapter-images, multipart with one file in <c>image</c>, through
/// the real model binding, for the novel's author only. The cover's checks (JPEG, PNG or WebP, at most 5 MB, its codes);
/// the whole picture turned upright, at most 2000 px on its long side, stored as a WebP without metadata under the
/// novel; the answer is { url }, which a chapter keeps as an image paragraph exactly as it is.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ChapterImageHttpTests(SardApiFactory api)
{
    private static readonly SKColor Red = new(220, 30, 30);
    private static readonly SKColor Blue = new(30, 60, 220);

    private static string PathOf(Guid novelId) => $"/api/novel/{novelId}/chapter-images";

    /// <summary>The form the app sends: the file in <c>image</c>, with this declared type (null: none).</summary>
    private static MultipartFormDataContent Form(byte[] bytes, string? contentType, string fileName = "picture.jpg")
    {
        var file = new ByteArrayContent(bytes);
        if (contentType is not null)
        {
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }
        return new MultipartFormDataContent { { file, "image", fileName } };
    }

    private static byte[] Png(int width = 800, int height = 600) =>
        TestImages.Halves(width, height, Red, Blue, SKEncodedImageFormat.Png);

    private Task<HttpResponseMessage> Upload(ApiUser? user, Guid novelId, HttpContent form) =>
        api.Send(HttpMethod.Post, PathOf(novelId), user, form);

    /// <summary>The address a successful upload answers; it must be the answer's only field.</summary>
    private static async Task<string> UrlOf(HttpResponseMessage response)
    {
        var body = await response.OkJson();
        Assert.Equal(["url"], body.EnumerateObject().Select(p => p.Name));
        return body.GetProperty("url").GetString()!;
    }

    private (byte[] Bytes, string ContentType) Stored(string url) => api.Storage.Objects[api.Storage.KeyOf(url)!];

    /// <summary>The pictures stored for a novel.</summary>
    private List<string> PicturesOf(Guid novelId) =>
        api.Storage.Objects.Keys.Where(k => k.StartsWith(ChapterImages.NovelPrefix(novelId))).ToList();

    private static async Task Refused(HttpResponseMessage response, HttpStatusCode status, string code, string message)
    {
        var body = await response.Error(status);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(message, body.GetProperty("message").GetString());
    }

    // ─── Storing it ───

    [Fact]
    public async Task The_author_gets_the_address_of_a_webp_stored_under_the_novel()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);

        var url = await UrlOf(await Upload(author, novel.Id, Form(Png(), "image/png", "map.png")));

        Assert.Matches($"^https://files\\.test/chapter-images/{novel.Id}/[0-9a-f]{{32}}\\.webp$", url);
        var (bytes, contentType) = Stored(url);
        Assert.Equal("image/webp", contentType);
        // "VP8 " is the simple lossy format: no VP8X header, so no EXIF, XMP, ICC or alpha chunks.
        Assert.Equal("VP8 ", Encoding.ASCII.GetString(bytes, 12, 4));
        using var stored = TestImages.Decode(bytes);
        Assert.Equal((800, 600), (stored.Width, stored.Height)); // within 2000 px: kept at its size
        Assert.True(TestImages.Near(stored.GetPixel(200, 300), Red), $"left is {stored.GetPixel(200, 300)}");
        Assert.True(TestImages.Near(stored.GetPixel(600, 300), Blue), $"right is {stored.GetPixel(600, 300)}");

        // Each upload is a new file; nothing is overwritten.
        var again = await UrlOf(await Upload(author, novel.Id, Form(Png(), "image/png", "map.png")));
        Assert.NotEqual(url, again);
        Assert.Equal(2, PicturesOf(novel.Id).Count);
    }

    [Fact]
    public async Task A_large_phone_photo_is_turned_upright_reduced_to_2000_px_and_loses_its_exif_and_gps()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        // Stored landscape 3000x2000 (left half red, right half blue) with EXIF orientation 6, a camera make and a GPS
        // pointer: viewers show it 2000x3000, red on top.
        var photo = TestImages.WithExifOrientation(TestImages.Halves(3000, 2000, Red, Blue, SKEncodedImageFormat.Jpeg), 6);
        Assert.Contains("Exif", Encoding.ASCII.GetString(photo));
        Assert.Contains("Phone", Encoding.ASCII.GetString(photo));

        var url = await UrlOf(await Upload(author, novel.Id, Form(photo, "image/jpeg", "IMG_0001.jpg")));

        var (bytes, _) = Stored(url);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.DoesNotContain("Exif", text);
        Assert.DoesNotContain("Phone", text);
        Assert.Equal("VP8 ", text.Substring(12, 4));
        using var stored = TestImages.Decode(bytes);
        Assert.Equal((1333, 2000), (stored.Width, stored.Height));
        Assert.True(TestImages.Near(stored.GetPixel(666, 500), Red), $"top is {stored.GetPixel(666, 500)}");
        Assert.True(TestImages.Near(stored.GetPixel(666, 1500), Blue), $"bottom is {stored.GetPixel(666, 1500)}");
    }

    [Fact]
    public async Task The_author_of_a_draft_novel_can_upload()
    {
        var author = await api.SignUp();
        var draft = await api.AddNovel(author, isDraft: true);

        var url = await UrlOf(await Upload(author, draft.Id, Form(Png(), "image/png")));

        Assert.StartsWith($"https://files.test/chapter-images/{draft.Id}/", url);
    }

    // ─── Who may upload ───

    [Fact]
    public async Task Another_member_is_403_NotOwner_and_nothing_is_stored()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var stranger = await api.SignUp();

        await Refused(await Upload(stranger, novel.Id, Form(Png(), "image/png")),
            HttpStatusCode.Forbidden, "NotOwner", "هذا الإجراء متاح لكاتب الرواية فقط");
        Assert.Empty(PicturesOf(novel.Id));
    }

    [Fact]
    public async Task Signed_out_is_401_and_nothing_is_stored()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);

        var response = await Upload(null, novel.Id, Form(Png(), "image/png"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(PicturesOf(novel.Id));
    }

    [Fact]
    public async Task An_unknown_or_deleted_novel_is_404_NovelNotFound()
    {
        var author = await api.SignUp();
        await Refused(await Upload(author, Guid.NewGuid(), Form(Png(), "image/png")),
            HttpStatusCode.NotFound, "NovelNotFound", "الرواية غير موجودة");

        var novel = await api.AddNovel(author);
        await (await api.Send(HttpMethod.Delete, $"/api/myworks/{novel.Id}/delete", author)).OkJson();
        await Refused(await Upload(author, novel.Id, Form(Png(), "image/png")),
            HttpStatusCode.NotFound, "NovelNotFound", "الرواية غير موجودة");
        Assert.Empty(PicturesOf(novel.Id));
    }

    // ─── The file ───

    [Fact]
    public async Task No_file_is_ValidationFailed()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);

        await Refused(await Upload(author, novel.Id, ReaderApi.Form(("caption", "خريطة"))),
            HttpStatusCode.BadRequest, ValidationProblems.Code, ChapterImages.RequiredMessage);
    }

    [Fact]
    public async Task A_request_that_is_not_multipart_is_415()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);

        var response = await Upload(author, novel.Id, JsonContent.Create(new { image = "https://example.test/a.png" }));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_file_over_5_MB_is_ValidationFailed()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var tooLarge = new byte[5 * 1024 * 1024 + 1];
        Png().CopyTo(tooLarge, 0);

        await Refused(await Upload(author, novel.Id, Form(tooLarge, "image/png")),
            HttpStatusCode.BadRequest, ValidationProblems.Code, ChapterImages.InvalidMessage);
        Assert.Empty(PicturesOf(novel.Id));
    }

    [Fact]
    public async Task A_file_of_another_type_is_refused_by_its_declared_type_and_by_its_bytes()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");

        // Declared as a GIF, or with no type: refused before it is read.
        await Refused(await Upload(author, novel.Id, Form(gif, "image/gif", "anim.gif")),
            HttpStatusCode.BadRequest, ValidationProblems.Code, ChapterImages.InvalidMessage);
        await Refused(await Upload(author, novel.Id, Form(Png(), contentType: null)),
            HttpStatusCode.BadRequest, ValidationProblems.Code, ChapterImages.InvalidMessage);

        // Declared as a picture, but its bytes are something else: the cover's code, the picture's words.
        await Refused(await Upload(author, novel.Id, Form(gif, "image/png", "anim.png")),
            HttpStatusCode.BadRequest, CoverErrorCodes.UnsupportedFormat, "يجب أن تكون الصورة بصيغة JPEG أو PNG أو WebP (هذا الملف بصيغة Gif).");
        await Refused(await Upload(author, novel.Id, Form("<html>not a picture</html>"u8.ToArray(), "image/jpeg")),
            HttpStatusCode.BadRequest, CoverErrorCodes.UnsupportedFormat, "يجب أن تكون الصورة بصيغة JPEG أو PNG أو WebP.");
        Assert.Empty(PicturesOf(novel.Id));
    }

    [Fact]
    public async Task An_empty_file_is_not_a_picture()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);

        // As for a cover: an empty file is not a JPEG, PNG or WebP image.
        await Refused(await Upload(author, novel.Id, Form([], "image/png", "empty.png")),
            HttpStatusCode.BadRequest, CoverErrorCodes.UnsupportedFormat, ChapterImages.Refusals.NotAnImage);
        Assert.Empty(PicturesOf(novel.Id));
    }

    [Fact]
    public async Task A_corrupt_or_oversized_picture_is_refused_with_the_covers_code()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var jpeg = TestImages.Halves(900, 600, Red, Blue, SKEncodedImageFormat.Jpeg);

        var truncated = await (await Upload(author, novel.Id, Form(jpeg[..(jpeg.Length / 2)], "image/jpeg"))).Error(HttpStatusCode.BadRequest);
        Assert.Equal(CoverErrorCodes.Unreadable, truncated.GetProperty("code").GetString());
        Assert.StartsWith("تعذّرت قراءة الصورة", truncated.GetProperty("message").GetString());

        // A PNG whose header claims 400 megapixels is refused before anything is decoded.
        await Refused(await Upload(author, novel.Id, Form(TestImages.PngHeaderOnly(20000, 20000), "image/png")),
            HttpStatusCode.BadRequest, CoverErrorCodes.TooManyPixels, "أبعاد الصورة كبيرة جداً (20000×20000)؛ استخدم صورة أقل من 50 ميغابكسل.");
        Assert.Empty(PicturesOf(novel.Id));
    }

    [Fact]
    public async Task A_picture_that_cannot_be_stored_is_UploadFailed()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var storage = new StorageDown();
        await using var storageDown = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IObjectStorage>();
            services.AddSingleton<IObjectStorage>(storage);
        }));

        var request = new HttpRequestMessage(HttpMethod.Post, PathOf(novel.Id)) { Content = Form(Png(), "image/png") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", author.Token);
        var response = await storageDown.CreateClient().SendAsync(request);

        await Refused(response, HttpStatusCode.BadRequest, "UploadFailed", "تعذّر رفع الصورة، حاول مرة أخرى.");
        Assert.Equal(1, storage.Attempts);
    }

    // ─── In a chapter ───

    [Fact]
    public async Task The_address_saved_in_a_chapter_comes_back_as_an_image_paragraph_exactly()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var url = await UrlOf(await Upload(author, novel.Id, Form(Png(), "image/png", "map.png")));
        Assert.Equal(url, ChapterFormat.ImageAddress(url)); // format v1 keeps it as it is

        // Saved as the editor writes a picture: alone in its paragraph.
        var created = await (await api.Send(HttpMethod.Post, $"/api/novel/{novel.Id}/chapter", author, JsonContent.Create(new
        {
            status = ChapterStatuses.Published,
            title = "فصل بخريطة",
            content = $"<p>قبل الخريطة</p><p><img src=\"{url}\"></p><p>بعدها</p>"
        }))).OkJson();
        var chapterId = created.GetProperty("id").GetGuid();

        var authors = await (await api.Get($"/api/myworks/{novel.Id}/chapters/{chapterId}", author)).OkJson();
        var readers = await (await api.Get($"/api/novel/{novel.Id}/chapter/{chapterId}?prefetch=true")).OkJson();
        (string, string, string?)[] saved = [("text", "قبل الخريطة", null), ("image", url, null), ("text", "بعدها", null)];
        foreach (var chapter in new[] { created, authors, readers })
        {
            Assert.Equal(saved, Paragraphs(chapter));
        }

        // Saved again with a caption: the same picture, its address unchanged.
        await (await api.Send(HttpMethod.Patch, $"/api/novel/{novel.Id}/chapter/{chapterId}", author, JsonContent.Create(new
        {
            title = "فصل بخريطة",
            content = $"<p>قبل الخريطة</p><p data-kind=\"image\"><img src=\"{url}\">خريطة المدينة</p><p>بعدها</p>"
        }))).OkJson();
        var edited = await (await api.Get($"/api/myworks/{novel.Id}/chapters/{chapterId}", author)).OkJson();
        Assert.Equal(("image", url, "خريطة المدينة"), Paragraphs(edited)[1]);
    }

    // ─── Rate limit ───

    [Fact]
    public async Task Uploads_from_one_address_are_limited_to_30_in_10_minutes()
    {
        var client = api.ClientFrom("10.86.0.1");
        var novelId = Guid.NewGuid();

        // Counted per address before authentication, so signed-out attempts count too.
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(PathOf(novelId), Form([1], "image/png"))).StatusCode);
        }

        var refused = await client.PostAsync(PathOf(novelId), Form([1], "image/png"));
        await Refused(refused, HttpStatusCode.TooManyRequests, ErrorHandlingMiddleware.TooManyRequests, WebApplicationBuilderExtensions.TooManyRequestsMessage);
        Assert.NotNull(refused.Headers.RetryAfter);

        // Another address is not affected.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.ClientFrom("10.86.0.2").PostAsync(PathOf(novelId), Form([1], "image/png"))).StatusCode);
    }

    private static List<(string Kind, string Content, string? Caption)> Paragraphs(JsonElement chapter) =>
        chapter.GetProperty("paragraphs").EnumerateArray()
            .OrderBy(p => p.GetProperty("orderIndex").GetInt32())
            .Select(p => (p.GetProperty("contentType").GetString()!, p.GetProperty("content").GetString()!, p.GetProperty("caption").GetString()))
            .ToList();

    /// <summary>A bucket that is down: every write fails, as R2 unreachable does.</summary>
    private sealed class StorageDown : IObjectStorage
    {
        public int Attempts;

        public Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Attempts);
            return Task.FromException<string>(new HttpRequestException("R2 is down"));
        }

        public Task<byte[]?> GetAsync(string keyOrUrl, long maxBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string? KeyOf(string keyOrUrl) => throw new NotSupportedException();
    }
}
