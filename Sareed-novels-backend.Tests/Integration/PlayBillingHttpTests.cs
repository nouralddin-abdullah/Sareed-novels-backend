using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Application.Services;
using Application.Wallet.DTOs;
using Infrastructure.BackgroundJobs;
using Infrastructure.Persistence;
using Infrastructure.PlayBilling;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sareed_novels_backend.Controllers;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// GET /api/wallet/play-products and POST /api/wallet/play-purchase through the real pipeline (auth, routing, JSON),
/// as the Android app calls them. Without configuration billing is disabled; with it, Google Play is a fake.
/// </summary>
public class PlayBillingHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private static int nextIp;
    private readonly FakeGooglePlay google = new();

    private static string NewIp() => $"10.12.{Interlocked.Increment(ref nextIp) / 250}.{nextIp % 250 + 1}";

    /// <summary>The API with billing configured, talking to the fake Google Play.</summary>
    private WebApplicationFactory<Program> WithBilling() => api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<PlayBillingConnection>();
        services.AddSingleton(google.Connection());
        services.AddHttpClient<GooglePlayApi>().ConfigurePrimaryHttpMessageHandler(google.CreateHandler);
        foreach (var worker in services.Where(d => d.ImplementationType == typeof(PlayBillingWorker)).ToList())
        {
            services.Remove(worker);
        }
    }));

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Client-IP", NewIp());
        return client;
    }

    /// <summary>Signs up a new user; returns their access token and id.</summary>
    private async Task<(string Token, string UserId)> Register(HttpClient client)
    {
        var name = "u" + Guid.NewGuid().ToString("N")[..10];
        using var form = new MultipartFormDataContent
        {
            { new StringContent(name), "UserName" },
            { new StringContent($"{name}@example.test"), "Email" },
            { new StringContent("Correct-horse-1"), "Password" },
            { new StringContent(name), "DisplayName" }
        };
        var response = await client.PostAsync("/api/identity/Register", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(api.ConnectionString).Options);
        var userId = await db.Users.Where(u => u.UserName == name).Select(u => u.Id).SingleAsync();
        return (token, userId);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, string? token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Purchase(HttpClient client, string token, object body) =>
        Send(client, HttpMethod.Post, "/api/wallet/play-purchase", token, JsonContent.Create(body));

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Matches("[؀-ۿ]", body.GetProperty("message").GetString()!); // Arabic, for the user
        Assert.Equal(["code", "message"], body.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task The_play_endpoints_need_a_signed_in_user()
    {
        var client = Client(api);

        var catalog = await Send(client, HttpMethod.Get, "/api/wallet/play-products", token: null);
        var purchase = await Purchase(client, token: null!, new { productId = "points_1000", purchaseToken = "t" });

        Assert.Equal(HttpStatusCode.Unauthorized, catalog.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, purchase.StatusCode);
    }

    [Fact]
    public async Task A_suspension_refuses_the_play_endpoints_and_the_purchase_waits_until_it_is_lifted()
    {
        // Moderation (#10) meets Play Billing: a suspension refuses every token while it lasts, here as everywhere.
        await using var factory = WithBilling();
        var client = Client(factory);
        var (token, userId) = await Register(client);
        var bought = google.Buy(userId, "points_1000");
        var body = new { productId = "points_1000", purchaseToken = bought.Token, orderId = bought.OrderId };
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Get, "/api/wallet/play-products", token)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAccountSuspensionService>().SuspendAsync(userId, DateTime.UtcNow.AddDays(7)));
        }

        // The session from before, and a token issued after the suspension's cut-off, which only the suspension refuses.
        var later = TokenFactory.Write(userId, DateTime.UtcNow.AddMinutes(1), DateTime.UtcNow.AddDays(59),
            factory.Services.GetRequiredService<IConfiguration>()["Jwt:Key"]!);
        foreach (var session in new[] { token, later })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, HttpMethod.Get, "/api/wallet/play-products", session)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Purchase(client, session, body)).StatusCode);
        }

        // Nothing reached Google or the database, so the purchase is still unconsumed on Google Play.
        Assert.Empty(google.Requests);
        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(api.ConnectionString).Options))
        {
            Assert.False(await db.PlayPurchases.AnyAsync(p => p.PurchaseToken == bought.Token));
            Assert.False(await db.UserWallets.AnyAsync(w => w.UserId == userId && w.CurrentBalance != 0));
        }

        // Lifted, the same token works again and the app's retry credits the purchase.
        using (var scope = factory.Services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IAccountSuspensionService>().LiftAsync(userId));
        }
        var response = await Purchase(client, later, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var credited = await Json(response);
        Assert.Equal((1000, 1000m), (credited.GetProperty("pointsAdded").GetInt32(), credited.GetProperty("currentBalance").GetDecimal()));
    }

    [Fact]
    public async Task Without_configuration_the_catalog_is_off_and_purchases_answer_503()
    {
        var client = Client(api);
        var (token, userId) = await Register(client);

        var catalog = await Send(client, HttpMethod.Get, "/api/wallet/play-products", token);
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        var body = await Json(catalog);
        Assert.False(body.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, body.GetProperty("products").GetArrayLength());
        Assert.Equal("الشراء عبر Google Play غير متاح حاليًا. حاول لاحقًا.", body.GetProperty("message").GetString());
        Assert.Equal(PlayAccountId.For(userId), body.GetProperty("obfuscatedAccountId").GetString());

        var purchase = await Purchase(client, token, new { productId = "points_1000", purchaseToken = "some-token", orderId = "GPA.1" });
        await AssertError(purchase, HttpStatusCode.ServiceUnavailable, "BillingUnavailable");
    }

    [Fact]
    public async Task A_purchase_credits_the_signed_in_user_whatever_the_body_says()
    {
        await using var factory = WithBilling();
        var client = Client(factory);
        var (token, userId) = await Register(client);
        var (_, otherUserId) = await Register(client);

        var catalog = await Json(await Send(client, HttpMethod.Get, "/api/wallet/play-products", token));
        Assert.True(catalog.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, catalog.GetProperty("message").ValueKind);
        Assert.Equal(PlayAccountId.For(userId), catalog.GetProperty("obfuscatedAccountId").GetString());
        Assert.Equal([("points_500", 500), ("points_1000", 1000), ("points_2500", 2500), ("points_5000", 5000)],
            catalog.GetProperty("products").EnumerateArray().Select(p => (p.GetProperty("productId").GetString(), p.GetProperty("points").GetInt32())));
        Assert.All(catalog.GetProperty("products").EnumerateArray(), p => Assert.Equal(["productId", "points"], p.EnumerateObject().Select(f => f.Name)));

        var bought = google.Buy(userId, "points_1000");
        var body = new { productId = "points_1000", purchaseToken = bought.Token, orderId = bought.OrderId, userId = otherUserId };

        var response = await Purchase(client, token, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Json(response);
        Assert.Equal(1000, result.GetProperty("pointsAdded").GetInt32());
        Assert.Equal(1000m, result.GetProperty("currentBalance").GetDecimal());

        var replay = await Json(await Purchase(client, token, body));
        Assert.Equal((1000, 1000m), (replay.GetProperty("pointsAdded").GetInt32(), replay.GetProperty("currentBalance").GetDecimal()));

        var wallet = await Json(await Send(client, HttpMethod.Get, "/api/wallet", token));
        Assert.Equal(1000m, wallet.GetProperty("currentBalance").GetDecimal());
        var history = await Json(await Send(client, HttpMethod.Get, "/api/wallet/transactions", token));
        var entry = Assert.Single(history.GetProperty("transactions").EnumerateArray());
        Assert.Equal("PlayPurchase", entry.GetProperty("type").GetString());
        Assert.StartsWith("شراء 1000 نقطة عبر Google Play", entry.GetProperty("description").GetString());
        Assert.Equal(1, google.Purchases[bought.Token].ConsumptionState);

        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(api.ConnectionString).Options);
        Assert.False(await db.UserWallets.AnyAsync(w => w.UserId == otherUserId && w.CurrentBalance != 0));
    }

    [Fact]
    public async Task Refused_purchases_answer_a_stable_code_and_an_Arabic_message()
    {
        await using var factory = WithBilling();
        var client = Client(factory);
        var (token, userId) = await Register(client);
        var (otherToken, otherUserId) = await Register(client);

        await AssertError(await Purchase(client, token, new { productId = "points_7", purchaseToken = "t" }),
            HttpStatusCode.BadRequest, "UnknownProduct");
        await AssertError(await Send(client, HttpMethod.Post, "/api/wallet/play-purchase", token),
            HttpStatusCode.BadRequest, "InvalidRequest");
        await AssertError(await Send(client, HttpMethod.Post, "/api/wallet/play-purchase", token,
                new StringContent("{not json", Encoding.UTF8, "application/json")),
            HttpStatusCode.BadRequest, "InvalidRequest");
        await AssertError(await Purchase(client, token, new { productId = "points_1000" }),
            HttpStatusCode.BadRequest, "InvalidRequest");
        await AssertError(await Purchase(client, token, new { productId = "points_1000", purchaseToken = "unknown-token" }),
            HttpStatusCode.BadRequest, "PurchaseNotFound");

        var someoneElses = google.Buy(otherUserId);
        await AssertError(await Purchase(client, token, new { productId = "points_1000", purchaseToken = someoneElses.Token }),
            HttpStatusCode.Forbidden, "AccountMismatch");

        var pending = google.Buy(userId, change: p => p.PurchaseState = 2);
        await AssertError(await Purchase(client, token, new { productId = "points_1000", purchaseToken = pending.Token }),
            HttpStatusCode.Conflict, "PurchasePending");

        var mine = google.Buy(userId);
        Assert.Equal(HttpStatusCode.OK, (await Purchase(client, token, new { productId = "points_1000", purchaseToken = mine.Token })).StatusCode);
        await AssertError(await Purchase(client, otherToken, new { productId = "points_1000", purchaseToken = mine.Token }),
            HttpStatusCode.Conflict, "AlreadyUsedByAnotherUser");

        google.Intercept = _ => FakeGooglePlay.Error(HttpStatusCode.ServiceUnavailable, "backendError");
        await AssertError(await Purchase(client, token, new { productId = "points_500", purchaseToken = google.Buy(userId, "points_500").Token }),
            HttpStatusCode.ServiceUnavailable, "VerificationUnavailable");
    }

    [Theory]
    [MemberData(nameof(AllErrors))]
    public void Every_refusal_has_a_status_and_an_Arabic_message(PlayPurchaseError error)
    {
        var result = PlayPurchaseResult.Failed(error);

        Assert.Equal(error.ToString(), result.Code);
        Assert.Matches("[؀-ۿ]", result.Message);
        Assert.InRange(PlayBillingController.StatusCodeFor(error), 400, 503);
    }

    public static TheoryData<PlayPurchaseError> AllErrors() => new(Enum.GetValues<PlayPurchaseError>());
}
