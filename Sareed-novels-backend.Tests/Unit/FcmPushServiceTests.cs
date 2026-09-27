using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Infrastructure.Configuration;
using Infrastructure.Push;
using Sareed_novels_backend.Tests.Integration;

namespace Sareed_novels_backend.Tests.Unit;

public class FcmPushServiceTests
{
    private readonly FakeFcm fcm = new();

    private static PushMessage Message(string token = "device-token") => new(
        token,
        "ردّ جديد على تعليقك",
        "سارة رد على تعليقك",
        "social",
        "ReplyToComment:6f9619ff-8b86-d011-b42d-00cf4fc964ff",
        4,
        new Dictionary<string, string> { ["type"] = "ReplyToComment", ["commentId"] = "c1", ["unreadCount"] = "4" });

    [Fact]
    public async Task Sends_one_v1_message_with_the_service_account_token()
    {
        var result = await PushTesting.Service(fcm).SendAsync(Message(), CancellationToken.None);

        Assert.Equal(PushSendOutcome.Sent, result.Outcome);
        var request = Assert.Single(fcm.Requests);
        Assert.Equal("https://fcm.googleapis.com/v1/projects/test-project/messages:send", request.Uri.ToString());
        Assert.Equal("Bearer test-access-token", request.Authorization);

        var message = request.Message;
        Assert.Equal("device-token", message.GetProperty("token").GetString());
        Assert.Equal("ردّ جديد على تعليقك", message.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal("سارة رد على تعليقك", message.GetProperty("notification").GetProperty("body").GetString());
        Assert.Equal("c1", message.GetProperty("data").GetProperty("commentId").GetString());
        Assert.All(message.GetProperty("data").EnumerateObject(), p => Assert.Equal(JsonValueKind.String, p.Value.ValueKind));

        var android = message.GetProperty("android");
        Assert.Equal("HIGH", android.GetProperty("priority").GetString());
        Assert.Equal("ReplyToComment:6f9619ff-8b86-d011-b42d-00cf4fc964ff", android.GetProperty("collapse_key").GetString());
        Assert.Equal("social", android.GetProperty("notification").GetProperty("channel_id").GetString());
        Assert.Equal("ReplyToComment:6f9619ff-8b86-d011-b42d-00cf4fc964ff", android.GetProperty("notification").GetProperty("tag").GetString());
        Assert.Equal(4, android.GetProperty("notification").GetProperty("notification_count").GetInt32());

        var apns = message.GetProperty("apns");
        Assert.Equal("ReplyToComment:6f9619ff-8b86-d011-b42d-00cf4fc964ff", apns.GetProperty("headers").GetProperty("apns-collapse-id").GetString());
        Assert.Equal(4, apns.GetProperty("payload").GetProperty("aps").GetProperty("badge").GetInt32());
    }

    private static HttpResponseMessage Answer(string name) => name switch
    {
        "unregistered" => FakeFcm.Unregistered(),
        "invalid token" => FakeFcm.InvalidToken(),
        "invalid token field" => FakeFcm.Error(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "Invalid value", "INVALID_ARGUMENT", invalidField: "message.token"),
        "message too big" => FakeFcm.Error(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "Message is too big", "INVALID_ARGUMENT"),
        "bare 404" => FakeFcm.Error(HttpStatusCode.NotFound, "NOT_FOUND", "Requested entity was not found."),
        "sender id mismatch" => FakeFcm.Error(HttpStatusCode.Forbidden, "PERMISSION_DENIED", "SenderId mismatch", "SENDER_ID_MISMATCH"),
        "apns credentials" => FakeFcm.Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "APNs auth error", "THIRD_PARTY_AUTH_ERROR"),
        "permission denied" => FakeFcm.Error(HttpStatusCode.Forbidden, "PERMISSION_DENIED", "Permission 'cloudmessaging.messages.create' denied"),
        "unauthenticated" => FakeFcm.Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "Request had invalid authentication credentials."),
        "unavailable" => FakeFcm.Unavailable(),
        "internal" => FakeFcm.Error(HttpStatusCode.InternalServerError, "INTERNAL", "Internal error", "INTERNAL"),
        "quota exceeded" => FakeFcm.Error(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "Quota exceeded", "QUOTA_EXCEEDED"),
        "html 502" => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") },
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [InlineData("unregistered", PushSendOutcome.InvalidToken)]
    [InlineData("invalid token", PushSendOutcome.InvalidToken)]
    [InlineData("invalid token field", PushSendOutcome.InvalidToken)]
    [InlineData("message too big", PushSendOutcome.Failed)]
    // A 404 without FCM's UNREGISTERED verdict (e.g. a wrong project id) must not delete devices.
    [InlineData("bare 404", PushSendOutcome.Failed)]
    [InlineData("sender id mismatch", PushSendOutcome.Failed)]
    [InlineData("apns credentials", PushSendOutcome.Failed)]
    // Our own credentials or permissions: retried (and logged) in case they get fixed.
    [InlineData("permission denied", PushSendOutcome.RetryLater)]
    [InlineData("unauthenticated", PushSendOutcome.RetryLater)]
    [InlineData("unavailable", PushSendOutcome.RetryLater)]
    [InlineData("internal", PushSendOutcome.RetryLater)]
    [InlineData("quota exceeded", PushSendOutcome.RetryLater)]
    [InlineData("html 502", PushSendOutcome.RetryLater)]
    public async Task FCM_answers_become_send_outcomes(string answer, PushSendOutcome expected)
    {
        fcm.Respond = _ => Answer(answer);

        var result = await PushTesting.Service(fcm).SendAsync(Message(), CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.StartsWith("FCM ", result.Error);
    }

    [Fact]
    public async Task Retry_after_is_passed_on_and_rate_limiting_waits_at_least_a_minute()
    {
        fcm.Respond = _ => FakeFcm.Unavailable(retryAfter: TimeSpan.FromSeconds(90));
        Assert.Equal(TimeSpan.FromSeconds(90), (await PushTesting.Service(fcm).SendAsync(Message(), CancellationToken.None)).RetryAfter);

        fcm.Respond = _ => FakeFcm.Error(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "Quota exceeded", "QUOTA_EXCEEDED",
            retryAfter: TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromMinutes(1), (await PushTesting.Service(fcm).SendAsync(Message(), CancellationToken.None)).RetryAfter);
    }

    [Fact]
    public async Task Network_failures_and_timeouts_are_retried()
    {
        fcm.Respond = _ => throw new HttpRequestException("connection reset");
        var failed = await PushTesting.Service(fcm).SendAsync(Message(), CancellationToken.None);
        Assert.Equal(PushSendOutcome.RetryLater, failed.Outcome);
        Assert.Contains("connection reset", failed.Error);

        fcm.Respond = _ => FakeFcm.Ok();
        fcm.Delay = TimeSpan.FromSeconds(5);
        var slow = new FcmPushService(new HttpClient(fcm) { BaseAddress = FcmPushService.BaseAddress, Timeout = TimeSpan.FromMilliseconds(100) },
            FcmConnection.Enabled("test-project", new FakeFcmTokens()));
        var timedOut = await slow.SendAsync(Message(), CancellationToken.None);
        Assert.Equal(PushSendOutcome.RetryLater, timedOut.Outcome);
        Assert.Equal("FCM request timed out", timedOut.Error);
    }

    [Fact]
    public async Task No_access_token_means_retry_later_and_nothing_is_sent()
    {
        var tokens = new FakeFcmTokens { Failure = new InvalidOperationException("invalid_grant") };

        var result = await PushTesting.Service(fcm, tokens).SendAsync(Message(), CancellationToken.None);

        Assert.Equal(PushSendOutcome.RetryLater, result.Outcome);
        Assert.Contains("invalid_grant", result.Error);
        Assert.Empty(fcm.Requests);
    }

    [Fact]
    public async Task A_disabled_connection_sends_nothing_and_says_why()
    {
        var service = new FcmPushService(new HttpClient(fcm) { BaseAddress = FcmPushService.BaseAddress }, FcmConnection.Disabled("Fcm:ServiceAccountJson is not set"));

        var result = await service.SendAsync(Message(), CancellationToken.None);

        Assert.False(service.IsEnabled);
        Assert.Equal(PushSendOutcome.Failed, result.Outcome);
        Assert.Contains("Fcm:ServiceAccountJson is not set", result.Error);
        Assert.Empty(fcm.Requests);
    }

    private static string ServiceAccountJson(string projectId = "sard-app")
    {
        using var rsa = RSA.Create(2048);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = projectId,
            ["private_key_id"] = "0123456789abcdef",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = $"firebase-adminsdk@{projectId}.iam.gserviceaccount.com",
            ["client_id"] = "1234567890",
            ["token_uri"] = "https://oauth2.googleapis.com/token"
        });
    }

    [Fact]
    public void Without_credentials_push_is_disabled_with_a_reason()
    {
        var connection = FcmConnection.FromSettings(new FcmSettings());

        Assert.False(connection.IsEnabled);
        Assert.Equal("Fcm:ServiceAccountJson is not set", connection.DisabledReason);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"type":"authorized_user","client_id":"x","client_secret":"y","refresh_token":"z"}""")]
    public void A_key_that_is_not_a_service_account_disables_push(string json)
    {
        var connection = FcmConnection.FromSettings(new FcmSettings { ServiceAccountJson = json });

        Assert.False(connection.IsEnabled);
        Assert.StartsWith("Fcm:ServiceAccountJson is not a valid service account key", connection.DisabledReason);
    }

    [Fact]
    public void The_key_is_accepted_as_json_or_base64_and_the_project_defaults_to_the_keys()
    {
        var json = ServiceAccountJson("sard-app");

        var plain = FcmConnection.FromSettings(new FcmSettings { ServiceAccountJson = json });
        var base64 = FcmConnection.FromSettings(new FcmSettings { ServiceAccountJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) });
        var overridden = FcmConnection.FromSettings(new FcmSettings { ServiceAccountJson = json, ProjectId = " other-project " });

        Assert.True(plain.IsEnabled);
        Assert.Equal("sard-app", plain.ProjectId);
        Assert.True(base64.IsEnabled);
        Assert.Equal("sard-app", base64.ProjectId);
        Assert.Equal("other-project", overridden.ProjectId);
    }
}
