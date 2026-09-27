using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Infrastructure.PlayBilling;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// An in-memory Google Play Developer API (purchases.products.get / consume, voidedpurchases.list) behind an
/// HttpMessageHandler, so the real <see cref="GooglePlayApi"/> runs against it and tests never reach Google.
/// </summary>
public sealed class FakeGooglePlay
{
    public const string PackageName = "com.sardnovels.app";
    public const string AccessToken = "fake-access-token";

    public static readonly PlayProduct[] Catalog =
    [
        new("points_500", 500), new("points_1000", 1000), new("points_2500", 2500), new("points_5000", 5000)
    ];

    private readonly List<FakeVoidedPurchase> voided = new();
    private int nextOrder = 10000;

    public ConcurrentDictionary<string, FakePlayPurchase> Purchases { get; } = new();
    public ConcurrentQueue<FakePlayRequest> Requests { get; } = new();

    /// <summary>Answers a request instead of the fake when it returns a response (failure injection).</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Intercept { get; set; }

    public int VoidedPageSize { get; set; } = 1000;

    public HttpMessageHandler CreateHandler() => new Handler(this);

    public PlayBillingConnection Connection(bool allowTestPurchases = false) =>
        PlayBillingConnection.Enabled(PackageName, Catalog, new FakeTokens(), allowTestPurchases);

    public GooglePlayApi Api(PlayBillingConnection? connection = null) =>
        new(new HttpClient(CreateHandler()) { BaseAddress = GooglePlayApi.BaseAddress }, connection ?? Connection());

    /// <summary>A completed purchase, made in the app for <paramref name="userId"/> (the app set its obfuscatedAccountId).</summary>
    public FakePlayPurchase Buy(string userId, string productId = "points_1000", Action<FakePlayPurchase>? change = null)
    {
        var purchase = new FakePlayPurchase
        {
            Token = "tok." + Guid.NewGuid().ToString("N") + "-AO_J1" + Guid.NewGuid().ToString("N"),
            ProductId = productId,
            OrderId = $"GPA.3300-1234-5678-{Interlocked.Increment(ref nextOrder)}",
            ObfuscatedAccountId = PlayAccountId.For(userId),
            PurchaseTimeMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        change?.Invoke(purchase);
        Purchases[purchase.Token] = purchase;
        return purchase;
    }

    /// <summary>Google voids the purchase (a refund), recording it at <paramref name="recordedAt"/>.</summary>
    public void Void(string token, DateTime recordedAt, string? orderId = null, int reason = 1, int source = 0)
    {
        if (Purchases.TryGetValue(token, out var purchase))
        {
            purchase.PurchaseState = 1;
        }
        lock (voided)
        {
            voided.Add(new FakeVoidedPurchase(token, orderId ?? purchase?.OrderId, recordedAt, reason, source));
        }
    }

    public int Count(string method, string pathSuffix) =>
        Requests.Count(r => r.Method == method && r.Path.EndsWith(pathSuffix, StringComparison.Ordinal));

    public static HttpResponseMessage Error(HttpStatusCode status, string reason, string message = "error") =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                error = new { code = (int)status, message, errors = new[] { new { message, domain = "global", reason } } }
            }), Encoding.UTF8, "application/json")
        };

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        if (request.Headers.Authorization?.ToString() != $"Bearer {AccessToken}")
        {
            return Error(HttpStatusCode.Unauthorized, "authError", "Invalid Credentials");
        }

        // /androidpublisher/v3/applications/{package}/purchases/...
        var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (segments.Length < 5 || segments[0] != "androidpublisher" || segments[1] != "v3" || segments[2] != "applications" || segments[4] != "purchases")
        {
            return Error(HttpStatusCode.NotFound, "notFound");
        }
        if (segments[3] != PackageName)
        {
            return Error(HttpStatusCode.NotFound, "applicationNotFound", "No application was found for the given package name.");
        }

        if (segments.Length == 6 && segments[5] == "voidedpurchases" && request.Method == HttpMethod.Get)
        {
            return ListVoided(request.RequestUri);
        }

        // products/{productId}/tokens/{token}[:consume]
        if (segments.Length == 9 && segments[5] == "products" && segments[7] == "tokens")
        {
            var token = segments[8];
            var consume = token.EndsWith(":consume", StringComparison.Ordinal);
            if (consume)
            {
                token = token[..^":consume".Length];
            }

            if (!Purchases.TryGetValue(token, out var purchase))
            {
                return Error(HttpStatusCode.BadRequest, "invalid", "Invalid Value");
            }

            if (!consume && request.Method == HttpMethod.Get)
            {
                return Json(purchase.ToJson());
            }
            if (consume && request.Method == HttpMethod.Post)
            {
                lock (purchase)
                {
                    if (purchase.PurchaseState != 0 || purchase.ConsumptionState == 1)
                    {
                        return Error(HttpStatusCode.BadRequest, "invalidPurchaseState", "The purchase is not in a state that allows consumption.");
                    }
                    purchase.ConsumptionState = 1;
                    purchase.AcknowledgementState = 1;
                }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }

        return Error(HttpStatusCode.NotFound, "notFound");
    }

    private HttpResponseMessage ListVoided(Uri uri)
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var start = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(query["startTime"]!)).UtcDateTime;
        var offset = int.TryParse(query["token"], out var pageStart) ? pageStart : 0;

        List<FakeVoidedPurchase> matching;
        lock (voided)
        {
            matching = voided.Where(v => v.RecordedAt >= start).OrderBy(v => v.RecordedAt).ToList();
        }
        var page = matching.Skip(offset).Take(VoidedPageSize).ToList();
        var next = offset + page.Count < matching.Count ? (offset + page.Count).ToString() : null;

        return Json(new Dictionary<string, object?>
        {
            ["pageInfo"] = new { totalResults = matching.Count, resultPerPage = page.Count, startIndex = offset },
            ["tokenPagination"] = next is null ? null : new { nextPageToken = next },
            ["voidedPurchases"] = page.Select(v => new Dictionary<string, object?>
            {
                ["kind"] = "androidpublisher#voidedPurchase",
                ["purchaseToken"] = v.Token,
                ["orderId"] = v.OrderId,
                ["purchaseTimeMillis"] = "1700000000000",
                ["voidedTimeMillis"] = new DateTimeOffset(v.RecordedAt).ToUnixTimeMilliseconds().ToString(),
                ["voidedReason"] = v.Reason,
                ["voidedSource"] = v.Source
            }).ToList()
        });
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private sealed class Handler(FakeGooglePlay google) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            google.Requests.Enqueue(new FakePlayRequest(request.Method.Method, Uri.UnescapeDataString(request.RequestUri!.AbsolutePath),
                request.RequestUri.Query, request.Headers.Authorization?.ToString(), body, request.Content?.Headers.ContentLength));
            return google.Intercept?.Invoke(request) ?? google.Answer(request);
        }
    }

    private sealed record FakeVoidedPurchase(string Token, string? OrderId, DateTime RecordedAt, int Reason, int Source);
}

public sealed class FakeTokens : IPlayAccessTokenSource
{
    public int Calls;

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(FakeGooglePlay.AccessToken);
    }
}

public sealed record FakePlayRequest(string Method, string Path, string Query, string? Authorization, string? Body, long? ContentLength);

/// <summary>What purchases.products.get says about a purchase; tests change it to play out Google's side.</summary>
public sealed class FakePlayPurchase
{
    public string Token { get; set; } = default!;
    public string ProductId { get; set; } = default!;
    public string? OrderId { get; set; }
    public int PurchaseState { get; set; }
    public int ConsumptionState { get; set; }
    public int AcknowledgementState { get; set; }
    public int? PurchaseType { get; set; }
    public int? Quantity { get; set; }
    public string? ObfuscatedAccountId { get; set; }
    public long PurchaseTimeMillis { get; set; }

    public Dictionary<string, object?> ToJson()
    {
        var json = new Dictionary<string, object?>
        {
            ["kind"] = "androidpublisher#productPurchase",
            ["purchaseTimeMillis"] = PurchaseTimeMillis.ToString(), // int64 fields come as strings
            ["purchaseState"] = PurchaseState,
            ["consumptionState"] = ConsumptionState,
            ["developerPayload"] = "",
            ["orderId"] = OrderId,
            ["acknowledgementState"] = AcknowledgementState,
            ["productId"] = ProductId,
            ["regionCode"] = "EG"
        };
        // Absent unless set, as Google does: purchaseType for normal purchases, quantity when it is 1.
        if (PurchaseType is { } type) json["purchaseType"] = type;
        if (Quantity is { } quantity) json["quantity"] = quantity;
        if (ObfuscatedAccountId is { } account) json["obfuscatedExternalAccountId"] = account;
        return json;
    }
}
