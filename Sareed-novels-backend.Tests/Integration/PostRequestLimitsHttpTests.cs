using System.Net;
using System.Net.Http.Headers;
using Application.Posts;
using Sareed_novels_backend.Middlewares;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The request size limit a post's picture meets before the post rules (#43) see it, on a real Kestrel server
/// (TestServer enforces none). The API keeps ASP.NET Core's default, a request body of at most 30,000,000 bytes (IIS,
/// in production, has the same default, maxAllowedContentLength), and the form limits are far above it (128 MB), so a
/// picture well over the 5 MB rule still gets PostImageTooLarge rather than a refusal without a post code.
/// </summary>
public sealed class PostRequestLimitsHttpTests(KestrelApi kestrel) : IClassFixture<KestrelApi>
{
    private const int MaxRequestBodySize = 30_000_000;

    private SardApiFactory Api => kestrel.Api;

    /// <summary>A post with a JPEG of this many bytes, and the whole request's size.</summary>
    private static (MultipartFormDataContent Form, long RequestBytes) WithPicture(int bytes)
    {
        var form = ReaderApi.Form(("Content", "صورة كبيرة"));
        var image = new ByteArrayContent(new byte[bytes]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "Image", "picture.jpg");
        return (form, form.Headers.ContentLength!.Value);
    }

    private static async Task AssertPostImageTooLarge(HttpResponseMessage response)
    {
        var body = await response.Error(HttpStatusCode.BadRequest);
        Assert.Equal(PostRules.ImageTooLargeCode, body.GetProperty("code").GetString());
        Assert.Equal(PostRules.ImageTooLargeMessage, body.GetProperty("message").GetString());
        Assert.False(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task A_6_MB_picture_reaches_the_rules_and_is_PostImageTooLarge()
    {
        var member = await Api.SignUp();

        var response = await Api.Send(HttpMethod.Post, "/api/posts", member, WithPicture(6 * 1024 * 1024).Form);

        await AssertPostImageTooLarge(response);
    }

    [Fact]
    public async Task Up_to_30_000_000_bytes_the_rules_answer_and_beyond_the_server_refuses_the_request_before_them()
    {
        var member = await Api.SignUp();

        var (under, underBytes) = WithPicture(MaxRequestBodySize - 1000);
        Assert.InRange(underBytes, MaxRequestBodySize - 1000, MaxRequestBodySize);
        await AssertPostImageTooLarge(await Api.Send(HttpMethod.Post, "/api/posts", member, under));

        // Over the limit the server refuses the request as soon as it sees its length, and closes the connection. A
        // client still sending gets no answer at all (the connection breaks); one that waits for "100 Continue" first,
        // as this one does, reads ASP.NET's refusal of a form it couldn't read: ValidationFailed, no post code.
        var (over, overBytes) = WithPicture(MaxRequestBodySize);
        Assert.True(overBytes > MaxRequestBodySize);
        using var waitsForContinue = new HttpClient(new SocketsHttpHandler { Expect100ContinueTimeout = TimeSpan.FromMinutes(1) })
        {
            BaseAddress = Api.CreateClient().BaseAddress
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/posts") { Content = over };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", member.Token);
        request.Headers.ExpectContinue = true;

        var response = await waitsForContinue.SendAsync(request);

        var body = await response.Error(HttpStatusCode.BadRequest);
        Assert.Equal(ValidationProblems.Code, body.GetProperty("code").GetString());
        Assert.True(response.Headers.ConnectionClose);
    }
}

/// <summary>The API, with its own database, on Kestrel listening on a free local port.</summary>
public sealed class KestrelApi : IAsyncLifetime
{
    public SardApiFactory Api { get; } = new();

    public Task InitializeAsync()
    {
        Api.UseKestrel(0);
        Api.StartServer();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Api.DisposeAsync();
}
