using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Infrastructure.Push;

/// <summary>
/// Sends through the FCM HTTP v1 API (one message per token) with a service-account OAuth token; see
/// https://firebase.google.com/docs/cloud-messaging/send/v1-api and .../error-codes for the error handling.
/// </summary>
public sealed class FcmPushService(HttpClient http, FcmConnection connection) : IPushService
{
    public static readonly Uri BaseAddress = new("https://fcm.googleapis.com/");

    /// <summary>FCM asks for at least a minute before retrying when the sending quota is exceeded.</summary>
    internal static readonly TimeSpan QuotaRetryDelay = TimeSpan.FromMinutes(1);

    // Property names exactly as written below (the API's proto names), no naming policy.
    private static readonly JsonSerializerOptions JsonOptions = new();

    public bool IsEnabled => connection.IsEnabled;

    public async Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        if (!connection.IsEnabled)
        {
            return new(PushSendOutcome.Failed, $"push is disabled: {connection.DisabledReason}");
        }

        string accessToken;
        try
        {
            accessToken = await connection.Tokens!.GetAccessTokenAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new(PushSendOutcome.RetryLater, $"could not get an FCM access token (check Fcm:ServiceAccountJson): {ex.Message}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"v1/projects/{Uri.EscapeDataString(connection.ProjectId!)}/messages:send")
        {
            Content = JsonContent.Create(Body(message), options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return PushSendResult.Sent;
            }
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Classify(response.StatusCode, body, RetryAfter(response));
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return new(PushSendOutcome.RetryLater, ex is TaskCanceledException ? "FCM request timed out" : $"FCM request failed: {ex.Message}");
        }
    }

    /// <summary>The v1 request body (proto field names).</summary>
    internal static object Body(PushMessage message) => new
    {
        message = new
        {
            token = message.Token,
            notification = new { title = message.Title, body = message.Body },
            data = message.Data,
            android = new
            {
                priority = "HIGH",
                collapse_key = message.CollapseKey,
                notification = new
                {
                    channel_id = message.ChannelId,
                    // Replaces a shown notification with the same tag instead of stacking another (collapse_key only
                    // collapses messages FCM is still holding for an offline phone).
                    tag = message.CollapseKey,
                    notification_count = message.UnreadCount
                }
            },
            apns = new
            {
                headers = new Dictionary<string, string> { ["apns-collapse-id"] = message.CollapseKey },
                payload = new { aps = new { badge = message.UnreadCount, sound = "default" } }
            }
        }
    };

    internal static PushSendResult Classify(HttpStatusCode status, string body, TimeSpan? retryAfter)
    {
        var error = FcmError.Parse(body);
        var code = error.ErrorCode ?? error.Status;
        var description = $"FCM {(int)status} {code}: {error.Message}".TrimEnd(' ', ':');

        // Only FCM's own verdict on the token removes it: a bare 404 or 400 may be our misconfiguration instead
        // (a wrong project id would otherwise wipe every device).
        if (error.ErrorCode == "UNREGISTERED" || (code == "INVALID_ARGUMENT" && error.IsAboutTheToken))
        {
            return new(PushSendOutcome.InvalidToken, description);
        }
        if (status == HttpStatusCode.TooManyRequests || code == "QUOTA_EXCEEDED")
        {
            return new(PushSendOutcome.RetryLater, description, retryAfter > QuotaRetryDelay ? retryAfter : QuotaRetryDelay);
        }
        if ((int)status >= 500 || code is "UNAVAILABLE" or "INTERNAL")
        {
            return new(PushSendOutcome.RetryLater, description, retryAfter);
        }
        // The token belongs to another Firebase project, or the APNs credentials are wrong: not this token's fault
        // alone, and not going to change on a retry.
        if (code is "SENDER_ID_MISMATCH" or "THIRD_PARTY_AUTH_ERROR")
        {
            return new(PushSendOutcome.Failed, description);
        }
        // Our credentials (expired key, missing permission): retried in case they're fixed, and logged.
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new(PushSendOutcome.RetryLater, $"{description} (check the Fcm service account and its permissions)", retryAfter);
        }
        return new(PushSendOutcome.Failed, description);
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }
        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }

    /// <summary>The parts of a Google API error body we act on.</summary>
    internal sealed record FcmError(string? Status, string? Message, string? ErrorCode, IReadOnlyList<string> Fields)
    {
        public bool IsAboutTheToken =>
            Fields.Contains("message.token")
            || (Message?.Contains("registration token", StringComparison.OrdinalIgnoreCase) ?? false);

        public static FcmError Parse(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                {
                    return new(null, Shorten(body), null, []);
                }

                string? errorCode = null;
                var fields = new List<string>();
                if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (detail.TryGetProperty("errorCode", out var fcmCode))
                        {
                            errorCode ??= fcmCode.GetString();
                        }
                        if (detail.TryGetProperty("fieldViolations", out var violations) && violations.ValueKind == JsonValueKind.Array)
                        {
                            fields.AddRange(violations.EnumerateArray()
                                .Select(v => v.TryGetProperty("field", out var field) ? field.GetString() : null)
                                .OfType<string>());
                        }
                    }
                }
                return new(String(error, "status"), String(error, "message"), errorCode, fields);
            }
            catch (JsonException)
            {
                return new(null, Shorten(body), null, []);
            }
        }

        private static string? String(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static string Shorten(string text) => text.Length <= 200 ? text : text[..200];
    }
}
