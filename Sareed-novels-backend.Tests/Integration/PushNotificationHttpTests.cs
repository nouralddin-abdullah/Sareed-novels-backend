using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Services;
using Infrastructure.Persistence;
using Infrastructure.Push;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The real API with push switched on against <see cref="FakeFcm"/>, so the whole path runs: an API call creates a
/// notification, which queues a push that the background worker sends.
/// </summary>
public sealed class PushApiFactory : IAsyncLifetime
{
    private readonly SardApiFactory api = new();

    public FakeFcm Fcm { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;

    public string ConnectionString => api.ConnectionString;

    public Task InitializeAsync()
    {
        Factory = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<FcmConnection>();
            services.AddSingleton(FcmConnection.Enabled("test-project", new FakeFcmTokens()));
            services.AddHttpClient<IPushService, FcmPushService>().ConfigurePrimaryHttpMessageHandler(() => Fcm);
        }));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await api.DisposeAsync();
    }

    public ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);
}

public class PushNotificationHttpTests(PushApiFactory push) : IClassFixture<PushApiFactory>
{
    private static int nextIp;

    private HttpClient Client()
    {
        var ip = Interlocked.Increment(ref nextIp);
        var client = push.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Client-IP", $"10.13.{ip / 250}.{ip % 250 + 1}");
        return client;
    }

    private sealed record Account(string Id, string UserName, string Token);

    private async Task<Account> SignUp()
    {
        var userName = "u" + Guid.NewGuid().ToString("N")[..10];
        using var form = new MultipartFormDataContent
        {
            { new StringContent(userName), "UserName" },
            { new StringContent($"{userName}@example.test"), "Email" },
            { new StringContent("Correct-horse-1"), "Password" },
            { new StringContent(userName), "DisplayName" }
        };
        var response = await Client().PostAsync("/api/identity/Register", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        await using var db = push.CreateContext();
        var id = await db.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync();
        return new Account(id, userName, token);
    }

    private static HttpRequestMessage As(Account? account, HttpMethod method, string url, object? json = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = json is null ? null : JsonContent.Create(json) };
        if (account is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        }
        return request;
    }

    private static string FcmToken() => $"{Guid.NewGuid():N}:APA91b{Guid.NewGuid():N}";

    private async Task<List<(string UserId, string Platform, string? AppVersion)>> DevicesWith(string token)
    {
        await using var db = push.CreateContext();
        return (await db.UserDevices.Where(d => d.Token == token).ToListAsync())
            .Select(d => (d.UserId, d.Platform, d.AppVersion)).ToList();
    }

    [Theory]
    [InlineData("POST", "/api/notifications/devices")]
    [InlineData("DELETE", "/api/notifications/devices/some-token")]
    [InlineData("GET", "/api/notifications/preferences")]
    [InlineData("PATCH", "/api/notifications/preferences")]
    public async Task Device_and_preference_endpoints_need_a_signed_in_user(string method, string url)
    {
        var response = await Client().SendAsync(As(null, new HttpMethod(method), url, new { token = "t", platform = "android" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Device_and_preference_endpoints_refuse_a_token_revoked_by_sign_out_everywhere()
    {
        var account = await SignUp();
        // Issued a minute ago: a revocation refuses the tokens issued before its second.
        var jwtKey = push.Factory.Services.GetRequiredService<IConfiguration>()["Jwt:Key"]!;
        var earlier = account with { Token = TokenFactory.Write(account.Id, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddDays(59), jwtKey) };
        var client = Client();
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(As(earlier, HttpMethod.Get, "/api/notifications/preferences"))).StatusCode);

        using (var scope = push.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITokenRevocationService>().RevokeAllTokensAsync(account.Id);
        }

        foreach (var (method, url) in new[]
                 {
                     (HttpMethod.Post, "/api/notifications/devices"), (HttpMethod.Delete, "/api/notifications/devices/some-token"),
                     (HttpMethod.Get, "/api/notifications/preferences"), (HttpMethod.Patch, "/api/notifications/preferences")
                 })
        {
            var response = await client.SendAsync(As(earlier, method, url, new { token = FcmToken(), platform = "android", social = false }));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Registering_the_same_token_twice_keeps_one_device_and_another_account_takes_it_over()
    {
        var (first, second) = (await SignUp(), await SignUp());
        var token = FcmToken();
        var client = Client();

        var registered = await client.SendAsync(As(first, HttpMethod.Post, "/api/notifications/devices", new { token, platform = "android", appVersion = "1.0.0" }));
        var again = await client.SendAsync(As(first, HttpMethod.Post, "/api/notifications/devices", new { token, platform = "Android", appVersion = "1.0.1" }));

        Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal((first.Id, "android", "1.0.1"), Assert.Single(await DevicesWith(token)));

        var takenOver = await client.SendAsync(As(second, HttpMethod.Post, "/api/notifications/devices", new { token, platform = "android" }));

        Assert.Equal(HttpStatusCode.NoContent, takenOver.StatusCode);
        Assert.Equal(second.Id, Assert.Single(await DevicesWith(token)).UserId);
    }

    [Theory]
    [InlineData(null, "android")]
    [InlineData("", "android")]
    [InlineData("has space", "android")]
    [InlineData("has/slash", "android")]
    [InlineData("valid-token", "windows")]
    [InlineData("valid-token", null)]
    public async Task A_registration_without_a_usable_token_or_platform_is_a_400(string? token, string? platform)
    {
        var account = await SignUp();

        var response = await Client().SendAsync(As(account, HttpMethod.Post, "/api/notifications/devices", new { token, platform }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unregistering_is_idempotent_and_leaves_other_accounts_tokens_alone()
    {
        var (me, other) = (await SignUp(), await SignUp());
        var (mine, theirs) = (FcmToken(), FcmToken());
        var client = Client();
        await client.SendAsync(As(me, HttpMethod.Post, "/api/notifications/devices", new { token = mine, platform = "android" }));
        await client.SendAsync(As(other, HttpMethod.Post, "/api/notifications/devices", new { token = theirs, platform = "android" }));

        foreach (var url in new[] { $"/api/notifications/devices/{Uri.EscapeDataString(mine)}", $"/api/notifications/devices/{mine}",
                     $"/api/notifications/devices/{Uri.EscapeDataString(theirs)}" })
        {
            var response = await client.SendAsync(As(me, HttpMethod.Delete, url));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Empty(await DevicesWith(mine));
        Assert.Single(await DevicesWith(theirs));
    }

    [Fact]
    public async Task Registering_devices_is_rate_limited_per_ip()
    {
        var account = await SignUp();
        var client = Client();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 31; i++)
        {
            var response = await client.SendAsync(As(account, HttpMethod.Post, "/api/notifications/devices", new { token = FcmToken(), platform = "android" }));
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(30), s => Assert.Equal(HttpStatusCode.NoContent, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[30]);
    }

    [Fact]
    public async Task Preferences_start_all_on_and_a_patch_changes_only_the_groups_it_names()
    {
        var account = await SignUp();
        var client = Client();

        var defaults = await client.SendAsync(As(account, HttpMethod.Get, "/api/notifications/preferences"));
        Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);
        Assert.Equal("""{"social":true,"chapters":true,"support":true}""", await defaults.Content.ReadAsStringAsync());

        var patched = await client.SendAsync(As(account, HttpMethod.Patch, "/api/notifications/preferences", new { chapters = false }));
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal("""{"social":true,"chapters":false,"support":true}""", await patched.Content.ReadAsStringAsync());

        var after = await client.SendAsync(As(account, HttpMethod.Get, "/api/notifications/preferences"));
        Assert.Equal("""{"social":true,"chapters":false,"support":true}""", await after.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_new_follower_reaches_the_followed_users_phone_as_a_push()
    {
        var (author, reader) = (await SignUp(), await SignUp());
        var token = FcmToken();
        var client = Client();
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.SendAsync(As(author, HttpMethod.Post, "/api/notifications/devices", new { token, platform = "android" }))).StatusCode);

        var follow = await client.SendAsync(As(reader, HttpMethod.Post, "/api/User/follow", new { userIdToFollow = author.Id }));
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);

        CapturedPush? sent = null;
        for (var waited = 0; sent is null && waited < 150; waited++)
        {
            sent = push.Fcm.Requests.FirstOrDefault(p => p.Token == token);
            if (sent is null)
            {
                await Task.Delay(100);
            }
        }

        Assert.NotNull(sent);
        Assert.Equal("متابع جديد", sent.Title);
        Assert.Equal("social", sent.ChannelId);
        Assert.Equal("NewFollower", sent.Data["type"]);
        Assert.Equal(reader.Id, sent.Data["actorId"]);
        Assert.Equal(reader.UserName, sent.Data["actorUserName"]);
        Assert.Equal("1", sent.Data["unreadCount"]);
    }
}
