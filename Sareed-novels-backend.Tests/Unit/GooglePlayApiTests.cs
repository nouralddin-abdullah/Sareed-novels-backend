using System.Net;
using System.Text;
using Infrastructure.PlayBilling;
using Sareed_novels_backend.Tests.Integration;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>The Google Play Developer API client against a fake handler: requests, parsing, and how failures are classified.</summary>
public class GooglePlayApiTests
{
    private readonly FakeGooglePlay google = new();

    private static HttpResponseMessage Body(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    [Fact]
    public async Task Get_asks_Google_for_this_apps_purchase_with_the_service_account_token_and_reads_it()
    {
        var purchase = google.Buy("user-1", "points_2500", p =>
        {
            p.PurchaseTimeMillis = 1_700_000_000_123;
            p.PurchaseType = 1;
        });

        var read = await google.Api().GetProductPurchaseAsync("points_2500", purchase.Token);

        var request = Assert.Single(google.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal($"/androidpublisher/v3/applications/com.sardnovels.app/purchases/products/points_2500/tokens/{purchase.Token}", request.Path);
        Assert.Equal("Bearer fake-access-token", request.Authorization);

        Assert.NotNull(read);
        Assert.Equal(("points_2500", purchase.OrderId, 0, 0, 1), (read.ProductId, read.OrderId, read.PurchaseState, read.ConsumptionState, read.PurchaseType));
        Assert.Equal(PlayAccountId.For("user-1"), read.ObfuscatedExternalAccountId);
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20, 123, DateTimeKind.Utc), read.PurchasedAt);
        Assert.Null(read.Quantity);
        Assert.Equal(1, read.QuantityOrOne);
        Assert.False(read.IsTestPurchase);
        Assert.False(read.IsConsumed);
    }

    [Fact]
    public async Task Path_segments_are_escaped()
    {
        string? requested = null;
        google.Intercept = request =>
        {
            requested = request.RequestUri!.AbsoluteUri;
            return FakeGooglePlay.Error(HttpStatusCode.BadRequest, "invalid");
        };

        await google.Api().GetProductPurchaseAsync("points_500", "a/b?c#d e");

        Assert.EndsWith("/purchases/products/points_500/tokens/a%2Fb%3Fc%23d%20e", requested);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid")]
    [InlineData(HttpStatusCode.NotFound, "purchaseTokenNotFound")]
    [InlineData(HttpStatusCode.Gone, "purchaseTokenNoLongerValid")]
    public async Task A_token_Google_does_not_know_reads_as_no_purchase(HttpStatusCode status, string reason)
    {
        google.Intercept = _ => FakeGooglePlay.Error(status, reason);

        Assert.Null(await google.Api().GetProductPurchaseAsync("points_500", "token"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authError", true)]
    [InlineData(HttpStatusCode.Forbidden, "permissionDenied", true)]
    [InlineData(HttpStatusCode.NotFound, "applicationNotFound", true)]
    [InlineData(HttpStatusCode.InternalServerError, "backendError", false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "backendError", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "rateLimitExceeded", false)]
    public async Task Setup_problems_and_outages_throw_saying_which_they_are(HttpStatusCode status, string reason, bool isConfigurationError)
    {
        google.Intercept = _ => FakeGooglePlay.Error(status, reason, "details from Google");

        var error = await Assert.ThrowsAsync<PlayApiException>(() => google.Api().GetProductPurchaseAsync("points_500", "token"));

        Assert.Equal(isConfigurationError, error.IsConfigurationError);
        Assert.Equal((int)status, error.StatusCode);
        Assert.Equal(reason, error.Reason);
        Assert.Contains("details from Google", error.Message);
    }

    [Fact]
    public async Task An_unknown_package_is_a_setup_problem_whatever_Google_calls_the_reason()
    {
        google.Intercept = _ => FakeGooglePlay.Error(HttpStatusCode.NotFound, "notFound", "No application was found for the given package name.");

        var error = await Assert.ThrowsAsync<PlayApiException>(() => google.Api().GetProductPurchaseAsync("points_500", "token"));

        Assert.True(error.IsConfigurationError);
    }

    [Fact]
    public async Task The_real_package_name_goes_to_Google()
    {
        var purchase = google.Buy("user-1");
        var elsewhere = PlayBillingConnection.Enabled("com.example.other", FakeGooglePlay.Catalog, new FakeTokens());

        // The fake knows only com.sardnovels.app, as Google knows only the apps the service account may see.
        var error = await Assert.ThrowsAsync<PlayApiException>(() => google.Api(elsewhere).GetProductPurchaseAsync("points_1000", purchase.Token));

        Assert.True(error.IsConfigurationError);
        Assert.Equal("applicationNotFound", error.Reason);
    }

    [Fact]
    public async Task An_error_page_that_is_not_JSON_is_still_an_outage()
    {
        google.Intercept = _ => Body(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>", "text/html");

        var error = await Assert.ThrowsAsync<PlayApiException>(() => google.Api().GetProductPurchaseAsync("points_500", "token"));

        Assert.False(error.IsConfigurationError);
        Assert.Equal(502, error.StatusCode);
        Assert.Null(error.Reason);
    }

    [Fact]
    public async Task An_unreadable_answer_is_an_outage()
    {
        google.Intercept = _ => Body(HttpStatusCode.OK, "{ not json");

        var error = await Assert.ThrowsAsync<PlayApiException>(() => google.Api().GetProductPurchaseAsync("points_500", "token"));

        Assert.False(error.IsConfigurationError);
    }

    [Fact]
    public async Task Network_failures_and_timeouts_are_outages()
    {
        google.Intercept = _ => throw new HttpRequestException("connection reset");
        Assert.False((await Assert.ThrowsAsync<PlayApiException>(() => google.Api().ConsumeAsync("points_500", "token"))).IsConfigurationError);

        // HttpClient reports its own timeout as a canceled task.
        google.Intercept = _ => throw new TaskCanceledException("timeout");
        Assert.False((await Assert.ThrowsAsync<PlayApiException>(() => google.Api().ConsumeAsync("points_500", "token"))).IsConfigurationError);
    }

    [Fact]
    public async Task A_canceled_call_is_not_mistaken_for_an_outage()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        google.Intercept = _ => throw new TaskCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => google.Api().ConsumeAsync("points_500", "token", canceled.Token));
    }

    [Fact]
    public async Task Consume_posts_an_empty_body_to_the_consume_method()
    {
        var purchase = google.Buy("user-1", "points_500");

        await google.Api().ConsumeAsync("points_500", purchase.Token);

        var request = Assert.Single(google.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal($"/androidpublisher/v3/applications/com.sardnovels.app/purchases/products/points_500/tokens/{purchase.Token}:consume", request.Path);
        Assert.Equal(0, request.ContentLength);
        Assert.Equal("", request.Body);
        Assert.Equal(1, google.Purchases[purchase.Token].ConsumptionState);
    }

    [Fact]
    public async Task A_refused_consume_throws()
    {
        var purchase = google.Buy("user-1", "points_500", p => p.PurchaseState = 1);

        var error = await Assert.ThrowsAsync<PlayApiException>(() => google.Api().ConsumeAsync("points_500", purchase.Token));

        Assert.Equal(400, error.StatusCode);
        Assert.False(error.IsConfigurationError);
    }

    [Fact]
    public async Task Voided_purchases_are_read_page_by_page_from_the_start_time()
    {
        google.VoidedPageSize = 2;
        var since = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var tokens = Enumerable.Range(0, 3).Select(i =>
        {
            var purchase = google.Buy("user-1");
            google.Void(purchase.Token, since.AddHours(i), reason: 7, source: 2);
            return purchase;
        }).ToList();

        var api = google.Api();
        var first = await api.ListVoidedPurchasesAsync(since, null);
        var second = await api.ListVoidedPurchasesAsync(since, first.NextPageToken);

        Assert.Equal("2", first.NextPageToken);
        Assert.Null(second.NextPageToken);
        Assert.Equal(tokens.Select(t => t.Token), first.Purchases.Concat(second.Purchases).Select(v => v.PurchaseToken));
        var voided = first.Purchases[1];
        Assert.Equal((tokens[1].OrderId, since.AddHours(1), 7, 2), (voided.OrderId, voided.VoidedAt, voided.VoidedReason, voided.VoidedSource));

        var requests = google.Requests.ToList();
        Assert.All(requests, r => Assert.Equal("/androidpublisher/v3/applications/com.sardnovels.app/purchases/voidedpurchases", r.Path));
        Assert.Equal($"?startTime={new DateTimeOffset(since).ToUnixTimeMilliseconds()}", requests[0].Query);
        Assert.Equal($"?startTime={new DateTimeOffset(since).ToUnixTimeMilliseconds()}&token=2", requests[1].Query);
    }

    [Fact]
    public async Task Voided_entries_without_a_token_are_skipped_and_an_empty_answer_is_no_voids()
    {
        google.Intercept = _ => Body(HttpStatusCode.OK,
            """{"voidedPurchases":[{"orderId":"GPA.1"},{"purchaseToken":"t2","voidedTimeMillis":"1700000000000"}],"tokenPagination":{}}""");
        var page = await google.Api().ListVoidedPurchasesAsync(DateTime.UtcNow.AddDays(-1), null);

        var only = Assert.Single(page.Purchases);
        Assert.Equal(("t2", (string?)null, (int?)null), (only.PurchaseToken, only.OrderId, only.VoidedReason));
        Assert.Null(page.NextPageToken);

        google.Intercept = _ => Body(HttpStatusCode.OK, "{}");
        Assert.Empty((await google.Api().ListVoidedPurchasesAsync(DateTime.UtcNow.AddDays(-1), null)).Purchases);
    }

    [Fact]
    public async Task With_billing_disabled_Google_is_never_called()
    {
        var api = google.Api(PlayBillingConnection.Disabled("PlayBilling:ServiceAccountJson is not set"));

        var error = await Assert.ThrowsAsync<PlayApiException>(() => api.GetProductPurchaseAsync("points_500", "token"));

        Assert.True(error.IsConfigurationError);
        Assert.Contains("ServiceAccountJson is not set", error.Message);
        Assert.Empty(google.Requests);
    }

    private sealed class FailingTokens(Exception error) : IPlayAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromException<string>(error);
    }

    [Fact]
    public async Task A_key_Google_refuses_is_a_setup_problem_and_an_unreachable_token_server_an_outage()
    {
        var refused = PlayBillingConnection.Enabled(FakeGooglePlay.PackageName, FakeGooglePlay.Catalog,
            new FailingTokens(new PlayApiException("invalid_grant", null, "invalid_grant", isConfigurationError: true)));
        var unreachable = PlayBillingConnection.Enabled(FakeGooglePlay.PackageName, FakeGooglePlay.Catalog,
            new FailingTokens(new HttpRequestException("oauth2.googleapis.com unreachable")));

        Assert.True((await Assert.ThrowsAsync<PlayApiException>(() => google.Api(refused).ConsumeAsync("points_500", "t"))).IsConfigurationError);
        Assert.False((await Assert.ThrowsAsync<PlayApiException>(() => google.Api(unreachable).ConsumeAsync("points_500", "t"))).IsConfigurationError);
        Assert.Empty(google.Requests);
    }
}
