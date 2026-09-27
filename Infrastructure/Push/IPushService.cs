namespace Infrastructure.Push;

/// <summary>Sends one push notification to one device.</summary>
public interface IPushService
{
    /// <summary>False when FCM credentials aren't configured: nothing can be sent.</summary>
    bool IsEnabled { get; }

    /// <summary>Never throws for a delivery problem: the result says whether to retry, drop the token, or give up.</summary>
    Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken);
}

/// <param name="Token">The device's FCM registration token.</param>
/// <param name="ChannelId">Android notification channel: one of <see cref="Domain.Constants.NotificationGroups"/>.</param>
/// <param name="CollapseKey">Notification type + target: a newer push with the same key replaces the older one.</param>
/// <param name="Data">String values only (an FCM rule): what the app needs to open the right screen.</param>
public sealed record PushMessage(
    string Token,
    string Title,
    string Body,
    string ChannelId,
    string CollapseKey,
    int UnreadCount,
    IReadOnlyDictionary<string, string> Data);

public enum PushSendOutcome
{
    Sent,
    /// <summary>The token is dead (app uninstalled, token expired or malformed): remove the device.</summary>
    InvalidToken,
    /// <summary>Temporary (FCM overloaded or rate limiting, network, credentials): try again later.</summary>
    RetryLater,
    /// <summary>FCM refused this message for good; retrying won't help.</summary>
    Failed
}

public sealed record PushSendResult(PushSendOutcome Outcome, string? Error = null, TimeSpan? RetryAfter = null)
{
    public static readonly PushSendResult Sent = new(PushSendOutcome.Sent);
}
