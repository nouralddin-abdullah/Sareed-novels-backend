using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Services;
using Infrastructure.BackgroundJobs;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The real API pipeline (routing, auth attributes, rate limiter, error middleware) on a throwaway SQL Server
/// database, with email and file storage replaced by in-memory fakes.
/// </summary>
public sealed class SardApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string DefaultServer = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";

    public string ConnectionString { get; } =
        $"{Environment.GetEnvironmentVariable("SARD_TEST_SQL") ?? DefaultServer};Database=SardApiTests_{Guid.NewGuid():N}";

    public FakeEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:SardDb", ConnectionString);
        builder.UseSetting("Jwt:Key", "test-only-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz");
        builder.UseSetting("Jwt:Issuer", "Sard");
        builder.UseSetting("Jwt:Audience", "Sardion");
        builder.UseSetting("CloudflareR2:AccessKey", "test");
        builder.UseSetting("CloudflareR2:SecretKey", "test");
        builder.UseSetting("CloudflareR2:BucketName", "test");
        builder.UseSetting("CloudflareR2:PublicUrl", "https://files.test");
        builder.UseSetting("Smtp:Password", "test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<IFileUploadService>();
            services.AddSingleton<IFileUploadService, FakeFileUploadService>();

            // Scheduled jobs aren't under test here.
            foreach (var job in services.Where(d => d.ServiceType == typeof(IHostedService)
                         && (d.ImplementationType == typeof(RankingRecalculationService) || d.ImplementationType == typeof(DailyPrivilegeUnlockService))).ToList())
            {
                services.Remove(job);
            }

            // TestServer has no client address; tests pick one per scenario so rate-limit partitions don't collide.
            services.AddSingleton<IStartupFilter, TestClientAddressFilter>();
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options))
        {
            await db.Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }

    public HttpClient ClientFrom(string ip)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressFilter.Header, ip);
        return client;
    }

    private sealed class TestClientAddressFilter : IStartupFilter
    {
        public const string Header = "X-Test-Client-IP";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var ip))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ip!);
                }
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Template)> Sent { get; } = new();

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        Sent.Enqueue((email, subject));
        return Task.CompletedTask;
    }

    public Task SendTemplateEmailAsync(string email, string templateId, object templateData)
    {
        Sent.Enqueue((email, templateId));
        return Task.CompletedTask;
    }
}

public sealed class FakeFileUploadService : IFileUploadService
{
    private static Task<string> Url(string folder, string owner) => Task.FromResult($"https://files.test/{folder}/{owner}/{Guid.NewGuid():N}.png");

    public Task<string> UploadImageAsync(Stream fileStream, string fileName, string contentType, string userId) => Url("profile-images", userId);
    public Task<string> UploadProfileBannerAsync(Stream fileStream, string contentType, string userId) => Url("profile-banners", userId);
    public Task<bool> DeleteImageAsync(string keyOrUrl) => Task.FromResult(true);
    public Task<string> UploadNovelImageAsync(Stream fileStream, string contentType, string novelId) => Url("novel-images", novelId);
    public Task<string> UploadCharacterImageAsync(Stream fileStream, string contentType, string characterId) => Url("characters-images", characterId);
    public Task<string> UploadCommentImageAsync(Stream fileStream, string contentType, string commentId) => Url("comment-images", commentId);
    public Task<string> UploadReadingListCoverImageAsync(Stream fileStream, string contentType, string readingListId) => Url("reading-list-images", readingListId);
    public Task<string> UploadPostImageAsync(Stream fileStream, string contentType, string postId) => Url("post-images", postId);
    public Task<string> UploadEntityGalleryImageAsync(Stream fileStream, string contentType, string entityId) => Url("entity-gallery", entityId);
    public Task<string> UploadPaymentProofAsync(Stream fileStream, string contentType, string userId) => Url("payment-proofs", userId);
    public Task<string> UploadGiftImageAsync(Stream fileStream, string contentType, string giftId) => Url("gift-images", giftId);
}

public class AccountSecurityHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private const string Password = "Correct-horse-1";
    private static int nextIp;

    private static string NewIp() => $"10.0.{Interlocked.Increment(ref nextIp) / 250}.{Interlocked.Increment(ref nextIp) % 250 + 1}";

    private static string NewName() => "u" + Guid.NewGuid().ToString("N")[..10];

    private static async Task<string> Register(HttpClient client, string userName)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(userName), "UserName" },
            { new StringContent($"{userName}@example.test"), "Email" },
            { new StringContent(Password), "Password" },
            { new StringContent(userName), "DisplayName" }
        };
        var response = await client.PostAsync("/api/identity/Register", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync("/api/identity/Login", new { loginCardinality = login, password });

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task Users_cannot_make_themselves_admin()
    {
        var client = api.ClientFrom(NewIp());
        var token = await Register(client, NewName());

        var promote = await client.SendAsync(Authorized(HttpMethod.Post, "/api/identity/make-admin", token));
        Assert.Equal(HttpStatusCode.NotFound, promote.StatusCode);

        var admin = await client.SendAsync(Authorized(HttpMethod.Get, "/api/admin/recharge/pending", token));
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/admin/recharge/pending")]
    [InlineData("GET", "/api/admin/withdraw/pending")]
    [InlineData("GET", "/api/admin/ranking-test/status")]
    [InlineData("POST", "/api/admin/ranking-test/calculate-all")]
    [InlineData("POST", "/api/library/admin/migrate-sequences")]
    [InlineData("POST", "/api/gift/admin/recalculate-weekly")]
    public async Task Admin_endpoints_need_an_admin(string method, string url)
    {
        var client = api.ClientFrom(NewIp());
        var anonymous = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var token = await Register(client, NewName());
        var user = await client.SendAsync(Authorized(new HttpMethod(method), url, token));
        Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        var name = NewName();
        await Register(api.ClientFrom(NewIp()), name);

        for (var i = 0; i < 5; i++)
        {
            // A fresh address per attempt: this is the per-account lockout, not the per-IP limit.
            Assert.Equal(HttpStatusCode.Forbidden, (await Login(api.ClientFrom(NewIp()), name, "wrong-password")).StatusCode);
        }

        var locked = await Login(api.ClientFrom(NewIp()), name, Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_failure_count()
    {
        var name = NewName();
        await Register(api.ClientFrom(NewIp()), name);

        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < 4; i++)
            {
                await Login(api.ClientFrom(NewIp()), name, "wrong-password");
            }
            Assert.Equal(HttpStatusCode.OK, (await Login(api.ClientFrom(NewIp()), name, Password)).StatusCode);
        }
    }

    [Fact]
    public async Task One_address_gets_ten_sign_in_attempts_a_minute()
    {
        var client = api.ClientFrom(NewIp());
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            statuses.Add((await Login(client, NewName(), "whatever")).StatusCode);
        }

        Assert.All(statuses.Take(10), s => Assert.Equal(HttpStatusCode.Forbidden, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);

        // Other clients are unaffected.
        Assert.Equal(HttpStatusCode.Forbidden, (await Login(api.ClientFrom(NewIp()), NewName(), "whatever")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_answers_the_same_for_unknown_emails()
    {
        var name = NewName();
        await Register(api.ClientFrom(NewIp()), name);
        var client = api.ClientFrom(NewIp());

        var known = await client.PostAsJsonAsync("/api/identity/forget-password", new { email = $"{name}@example.test" });
        var unknown = await client.PostAsJsonAsync("/api/identity/forget-password", new { email = $"nobody-{name}@example.test" });

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Contains(api.Emails.Sent, e => e.To == $"{name}@example.test" && e.Template == "reset-password");
        Assert.DoesNotContain(api.Emails.Sent, e => e.To == $"nobody-{name}@example.test");
    }

    [Fact]
    public async Task Confirmation_email_answers_the_same_for_unknown_emails()
    {
        var client = api.ClientFrom(NewIp());
        var name = NewName();

        var unknown = await client.PostAsJsonAsync("/api/identity/Send-Email", new { email = $"nobody-{name}@example.test" });

        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.DoesNotContain(api.Emails.Sent, e => e.To == $"nobody-{name}@example.test");
    }

    [Fact]
    public async Task Email_endpoints_allow_five_requests_per_address()
    {
        var client = api.ClientFrom(NewIp());
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/identity/forget-password", new { email = $"{NewName()}@example.test" })).StatusCode);
        }

        Assert.All(statuses.Take(5), s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[5]);
    }

    [Fact]
    public async Task Profile_links_cannot_use_javascript_urls()
    {
        var client = api.ClientFrom(NewIp());
        var token = await Register(client, NewName());

        var script = await client.SendAsync(Authorized(HttpMethod.Patch, "/api/User/update-me?FacebookUrl=javascript:alert(1)", token));
        var web = await client.SendAsync(Authorized(HttpMethod.Patch, "/api/User/update-me?FacebookUrl=https://facebook.com/someone&TwitterUrl=x.com/someone", token));

        Assert.Equal(HttpStatusCode.BadRequest, script.StatusCode);
        Assert.Equal(HttpStatusCode.OK, web.StatusCode);
    }

    [Fact]
    public async Task Authors_cannot_touch_chapters_of_other_novels()
    {
        var client = api.ClientFrom(NewIp());
        var victim = await Register(client, NewName());
        var attacker = await Register(client, NewName());
        var victimsNovel = await CreateNovel(client, victim, "رواية الضحية");
        var attackersNovel = await CreateNovel(client, attacker, "رواية المهاجم");

        var created = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/novel/{victimsNovel}/chapter", victim,
            JsonContent.Create(new { title = "مسودة", content = "<p>نص سري</p>", status = "Draft" })));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var chapterId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var read = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/myworks/{attackersNovel}/chapters/{chapterId}", attacker));
        var update = await client.SendAsync(Authorized(HttpMethod.Patch, $"/api/novel/{attackersNovel}/chapter/{chapterId}", attacker,
            JsonContent.Create(new { title = "hacked", content = "<p>defaced</p>", status = "Published" })));
        var delete = await client.SendAsync(Authorized(HttpMethod.Delete, $"/api/novel/{attackersNovel}/chapter/{chapterId}", attacker));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var own = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/myworks/{victimsNovel}/chapters/{chapterId}", victim));
        var chapter = await own.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("مسودة", chapter.GetProperty("title").GetString());
        Assert.Equal("Draft", chapter.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Chapter_status_must_be_draft_or_published()
    {
        var client = api.ClientFrom(NewIp());
        var author = await Register(client, NewName());
        var novel = await CreateNovel(client, author, "رواية الحالة");

        var response = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/novel/{novel}/chapter", author,
            JsonContent.Create(new { title = "فصل", content = "<p>نص</p>", status = "published" })));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string> CreateNovel(HttpClient client, string token, string title)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(title), "Title" },
            { new StringContent("ملخص الرواية"), "Summary" },
            { new StringContent("1"), "GenreIds" }
        };
        var cover = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        cover.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(cover, "CoverImageUrl", "cover.png");

        var response = await client.SendAsync(Authorized(HttpMethod.Post, "/api/myworks", token, form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("novelId").GetString()!;
    }
}
