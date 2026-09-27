using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Infrastructure.Persistence;
using Infrastructure.Push;
using Microsoft.Extensions.Options;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Stands in for fcm.googleapis.com: records every request and answers with <see cref="Respond"/> (200 by default).
/// Never talks to the real FCM.
/// </summary>
public sealed class FakeFcm : HttpMessageHandler
{
    private int inFlight;
    private int maxInFlight;

    public ConcurrentQueue<CapturedPush> Requests { get; } = new();

    /// <summary>The response to each request; defaults to FCM's success answer.</summary>
    public Func<CapturedPush, HttpResponseMessage> Respond { get; set; } = _ => Ok();

    /// <summary>How long each request takes (to observe parallelism).</summary>
    public TimeSpan Delay { get; set; }

    public int MaxInFlight => Volatile.Read(ref maxInFlight);

    public static HttpResponseMessage Ok() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"name":"projects/test-project/messages/0:1"}""")
    };

    /// <summary>A Google API error response as FCM sends it.</summary>
    public static HttpResponseMessage Error(HttpStatusCode status, string googleStatus, string message, string? fcmErrorCode = null,
        string? invalidField = null, TimeSpan? retryAfter = null)
    {
        var details = new List<object>();
        if (fcmErrorCode is not null)
        {
            details.Add(new Dictionary<string, object> { ["@type"] = "type.googleapis.com/google.firebase.fcm.v1.FcmError", ["errorCode"] = fcmErrorCode });
        }
        if (invalidField is not null)
        {
            details.Add(new Dictionary<string, object>
            {
                ["@type"] = "type.googleapis.com/google.rpc.BadRequest",
                ["fieldViolations"] = new[] { new { field = invalidField, description = message } }
            });
        }
        var body = JsonSerializer.Serialize(new { error = new { code = (int)status, message, status = googleStatus, details } });
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        if (retryAfter is { } wait)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
        }
        return response;
    }

    public static HttpResponseMessage Unregistered() =>
        Error(HttpStatusCode.NotFound, "NOT_FOUND", "Requested entity was not found.", "UNREGISTERED");

    public static HttpResponseMessage InvalidToken() =>
        Error(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "The registration token is not a valid FCM registration token", "INVALID_ARGUMENT");

    public static HttpResponseMessage Unavailable(TimeSpan? retryAfter = null) =>
        Error(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE", "The service is currently unavailable.", "UNAVAILABLE", retryAfter: retryAfter);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var now = Interlocked.Increment(ref inFlight);
        int seen;
        while (now > (seen = Volatile.Read(ref maxInFlight)) && Interlocked.CompareExchange(ref maxInFlight, now, seen) != seen)
        {
        }

        try
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var push = new CapturedPush(request.RequestUri!, request.Headers.Authorization?.ToString(), JsonDocument.Parse(body).RootElement.Clone());
            Requests.Enqueue(push);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }
            return Respond(push);
        }
        finally
        {
            Interlocked.Decrement(ref inFlight);
        }
    }
}

public sealed record CapturedPush(Uri Uri, string? Authorization, JsonElement Body)
{
    public JsonElement Message => Body.GetProperty("message");
    public string Token => Message.GetProperty("token").GetString()!;
    public string Title => Message.GetProperty("notification").GetProperty("title").GetString()!;
    public string Text => Message.GetProperty("notification").GetProperty("body").GetString()!;
    public string ChannelId => Message.GetProperty("android").GetProperty("notification").GetProperty("channel_id").GetString()!;
    public string CollapseKey => Message.GetProperty("android").GetProperty("collapse_key").GetString()!;

    public IReadOnlyDictionary<string, string> Data =>
        Message.GetProperty("data").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
}

public sealed class FakeFcmTokens : IFcmAccessTokenSource
{
    public const string Token = "test-access-token";

    public Exception? Failure { get; set; }

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
        Failure is null ? Task.FromResult(Token) : Task.FromException<string>(Failure);
}

/// <summary>A clock tests move forward.</summary>
public sealed class MutableClock(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;

    public void Advance(TimeSpan by) => UtcNow += by;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
}

internal static class PushTesting
{
    public static FcmPushService Service(FakeFcm fcm, FakeFcmTokens? tokens = null) =>
        new(new HttpClient(fcm) { BaseAddress = FcmPushService.BaseAddress }, FcmConnection.Enabled("test-project", tokens ?? new FakeFcmTokens()));

    public static PushOutboxProcessor Processor(ApplicationDbContext db, FakeFcm fcm, PushDeliveryOptions? options = null,
        TimeProvider? clock = null, ListLogger<PushOutboxProcessor>? logger = null) =>
        new(db, Service(fcm), new PushTargetResolver(db), Options.Create(options ?? new PushDeliveryOptions()),
            clock ?? TimeProvider.System, logger ?? new ListLogger<PushOutboxProcessor>());
}
