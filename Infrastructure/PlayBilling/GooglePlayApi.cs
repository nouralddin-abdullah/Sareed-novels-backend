using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Wallet.DTOs;

namespace Infrastructure.PlayBilling;

/// <summary>
/// The Google Play Developer API calls point packs need (androidpublisher v3), over plain HTTP with the service
/// account's token. Tests put a fake handler behind the HttpClient, so they never reach Google.
/// </summary>
public class GooglePlayApi(HttpClient http, PlayBillingConnection connection)
{
    public static readonly Uri BaseAddress = new("https://androidpublisher.googleapis.com/androidpublisher/v3/");

    // Google sends int64 fields (times in milliseconds) as JSON strings.
    private static readonly JsonSerializerOptions Json = new() { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    /// <summary>
    /// purchases.products.get. Null when Google doesn't know the token for this app and product (400, 404, 410): a
    /// token from another app or product, or a made-up one. Throws <see cref="PlayApiException"/> when Google can't
    /// answer or refuses our credentials.
    /// </summary>
    public async Task<PlayProductPurchase?> GetProductPurchaseAsync(string productId, string purchaseToken,
        CancellationToken cancellationToken = default)
    {
        const string operation = "purchases.products.get";
        using var response = await SendAsync(HttpMethod.Get, ProductPurchasePath(productId, purchaseToken), cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await ReadAsync<PlayProductPurchase>(response, operation, cancellationToken);
        }

        var error = await PlayApiException.FromResponseAsync(response, operation, cancellationToken);
        if (!error.IsConfigurationError
            && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return null;
        }
        throw error;
    }

    /// <summary>
    /// purchases.products.consume: the purchase is consumed (which also acknowledges it, so Google won't refund it
    /// after three days) and the user can buy the same pack again.
    /// </summary>
    public async Task ConsumeAsync(string productId, string purchaseToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, ProductPurchasePath(productId, purchaseToken) + ":consume", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PlayApiException.FromResponseAsync(response, "purchases.products.consume", cancellationToken);
        }
    }

    /// <summary>
    /// voidedpurchases.list: a page of the in-app purchases Google recorded as voided from <paramref name="startTime"/>
    /// (no more than 30 days ago) until now. Pass the previous page's <see cref="PlayVoidedPurchasesPage.NextPageToken"/>
    /// to get the next page.
    /// </summary>
    public async Task<PlayVoidedPurchasesPage> ListVoidedPurchasesAsync(DateTime startTime, string? pageToken,
        CancellationToken cancellationToken = default)
    {
        const string operation = "voidedpurchases.list";
        var path = $"applications/{Uri.EscapeDataString(connection.PackageName)}/purchases/voidedpurchases?startTime={ToUnixMillis(startTime)}";
        if (!string.IsNullOrEmpty(pageToken))
        {
            path += "&token=" + Uri.EscapeDataString(pageToken);
        }

        using var response = await SendAsync(HttpMethod.Get, path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PlayApiException.FromResponseAsync(response, operation, cancellationToken);
        }

        var body = await ReadAsync<VoidedPurchasesResponse>(response, operation, cancellationToken);
        var purchases = (body.VoidedPurchases ?? new List<VoidedPurchaseJson>())
            .Where(v => !string.IsNullOrEmpty(v.PurchaseToken))
            .Select(v => new PlayVoidedPurchase(v.PurchaseToken!, v.OrderId, FromUnixMillis(v.VoidedTimeMillis), v.VoidedReason, v.VoidedSource))
            .ToList();
        var next = body.TokenPagination?.NextPageToken;
        return new PlayVoidedPurchasesPage(purchases, string.IsNullOrEmpty(next) ? null : next);
    }

    private string ProductPurchasePath(string productId, string purchaseToken) =>
        $"applications/{Uri.EscapeDataString(connection.PackageName)}/purchases/products/{Uri.EscapeDataString(productId)}" +
        $"/tokens/{Uri.EscapeDataString(purchaseToken)}";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var tokens = connection.Tokens
            ?? throw new PlayApiException($"Google Play billing is disabled: {connection.DisabledReason}", null, null, isConfigurationError: true);

        string accessToken;
        try
        {
            accessToken = await tokens.GetAccessTokenAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not PlayApiException && !cancellationToken.IsCancellationRequested)
        {
            throw new PlayApiException($"No access token for the Google Play Developer API: {ex.Message}", null, null, isConfigurationError: false, ex);
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (method == HttpMethod.Post)
        {
            request.Content = new ByteArrayContent(Array.Empty<byte>()); // Content-Length: 0
        }

        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PlayApiException($"Google Play Developer API unreachable: {ex.Message}", null, null, isConfigurationError: false, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PlayApiException("Google Play Developer API timed out", null, null, isConfigurationError: false, ex);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken)
                ?? throw new JsonException("empty body");
        }
        catch (JsonException ex)
        {
            throw new PlayApiException($"{operation}: unreadable answer from Google ({ex.Message})", (int)response.StatusCode, null,
                isConfigurationError: false, ex);
        }
    }

    internal static long ToUnixMillis(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    internal static DateTime? FromUnixMillis(long? millis) =>
        millis is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;

    private sealed class VoidedPurchasesResponse
    {
        [JsonPropertyName("voidedPurchases")] public List<VoidedPurchaseJson>? VoidedPurchases { get; set; }
        [JsonPropertyName("tokenPagination")] public TokenPaginationJson? TokenPagination { get; set; }
    }

    private sealed class TokenPaginationJson
    {
        [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
    }

    private sealed class VoidedPurchaseJson
    {
        [JsonPropertyName("purchaseToken")] public string? PurchaseToken { get; set; }
        [JsonPropertyName("orderId")] public string? OrderId { get; set; }
        [JsonPropertyName("voidedTimeMillis")] public long? VoidedTimeMillis { get; set; }
        [JsonPropertyName("voidedReason")] public int? VoidedReason { get; set; }
        [JsonPropertyName("voidedSource")] public int? VoidedSource { get; set; }
    }
}

/// <summary>A ProductPurchase resource, as purchases.products.get returns it.</summary>
public sealed class PlayProductPurchase
{
    [JsonPropertyName("productId")] public string? ProductId { get; set; }
    [JsonPropertyName("orderId")] public string? OrderId { get; set; }
    [JsonPropertyName("purchaseTimeMillis")] public long? PurchaseTimeMillis { get; set; }

    /// <summary>0 purchased, 1 canceled, 2 pending.</summary>
    [JsonPropertyName("purchaseState")] public int? PurchaseState { get; set; }

    /// <summary>0 yet to be consumed, 1 consumed.</summary>
    [JsonPropertyName("consumptionState")] public int? ConsumptionState { get; set; }

    /// <summary>Absent for a normal purchase; 0 test (license tester), 1 promo code, 2 rewarded.</summary>
    [JsonPropertyName("purchaseType")] public int? PurchaseType { get; set; }

    /// <summary>0 yet to be acknowledged, 1 acknowledged.</summary>
    [JsonPropertyName("acknowledgementState")] public int? AcknowledgementState { get; set; }

    /// <summary>Absent means 1; more only when the product allows multi-quantity purchases.</summary>
    [JsonPropertyName("quantity")] public int? Quantity { get; set; }

    /// <summary>What the app passed as obfuscatedAccountId (<see cref="PlayAccountId"/>); absent if it passed none.</summary>
    [JsonPropertyName("obfuscatedExternalAccountId")] public string? ObfuscatedExternalAccountId { get; set; }

    [JsonPropertyName("regionCode")] public string? RegionCode { get; set; }

    public const int Purchased = 0;
    public const int Canceled = 1;
    public const int Pending = 2;

    public bool IsTestPurchase => PurchaseType == 0;
    public bool IsConsumed => ConsumptionState == 1;
    public int QuantityOrOne => Quantity ?? 1;
    public DateTime? PurchasedAt => GooglePlayApi.FromUnixMillis(PurchaseTimeMillis);
}

public sealed record PlayVoidedPurchasesPage(IReadOnlyList<PlayVoidedPurchase> Purchases, string? NextPageToken);

/// <summary>A Google Play Developer API call that failed, and whether retrying can help.</summary>
public sealed class PlayApiException(string message, int? statusCode, string? reason, bool isConfigurationError, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The HTTP status Google answered with; null when there was no answer (network, timeout, token).</summary>
    public int? StatusCode { get; } = statusCode;

    /// <summary>Google's error reason, e.g. permissionDenied, applicationNotFound.</summary>
    public string? Reason { get; } = reason;

    /// <summary>Our setup is wrong (key, permissions in Play Console, package name): retrying won't help until it's fixed.</summary>
    public bool IsConfigurationError { get; } = isConfigurationError;

    public static async Task<PlayApiException> FromResponseAsync(HttpResponseMessage response, string operation,
        CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        string? reason = null;
        string? message = null;
        try
        {
            // {"error": {"code": 404, "message": "...", "errors": [{"reason": "applicationNotFound", ...}], "status": "NOT_FOUND"}}
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
                    && errors.GetArrayLength() > 0 && errors[0].ValueKind == JsonValueKind.Object
                    && errors[0].TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    reason = r.GetString();
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            // No JSON error body (a proxy or load balancer answered); the status says enough.
        }

        // 401/403: the key or its Play Console permissions; applicationNotFound: the package name.
        var isConfigurationError = status is 401 or 403 || reason == "applicationNotFound"
            || (status == 404 && message?.Contains("package name", StringComparison.OrdinalIgnoreCase) == true);
        return new PlayApiException($"{operation} failed: HTTP {status}{(reason is null ? "" : $" {reason}")}{(message is null ? "" : $": {message}")}",
            status, reason, isConfigurationError);
    }
}
