using Domain.Entities;

namespace Infrastructure.Push;

/// <summary>
/// One push notification waiting to go (or gone) to one device: the durable queue between creating a notification
/// and sending it through FCM, so a push survives an app-pool recycle. Rows are written in the same save as the
/// notification (<see cref="Repositories.NotificationsRepository"/>) and drained by <see cref="PushOutboxProcessor"/>;
/// finished rows are deleted after a few days.
/// </summary>
public class PushOutboxMessage
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }
    /// <summary>The <see cref="UserDevice"/> to send to. No foreign key: a device can go away while this waits.</summary>
    public Guid DeviceId { get; set; }
    public PushOutboxStatus Status { get; set; }
    /// <summary>How many times a worker has picked this up (counted when claimed, so a crash counts too).</summary>
    public int Attempts { get; set; }
    /// <summary>When the row is due; while a worker holds it, the end of its lease.</summary>
    public DateTime NextAttemptAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>Why the last attempt failed, or why the push was skipped.</summary>
    public string? LastError { get; set; }

    public const int LastErrorMaxLength = 500;

    public static PushOutboxMessage For(Notification notification, Guid deviceId) => new()
    {
        NotificationId = notification.Id,
        DeviceId = deviceId,
        Status = PushOutboxStatus.Pending,
        CreatedAt = notification.CreatedAt,
        NextAttemptAt = notification.CreatedAt
    };
}

public enum PushOutboxStatus : byte
{
    Pending = 0,
    Sent = 1,
    /// <summary>FCM refused it for good (invalid token, bad request) or it kept failing until the last attempt.</summary>
    Failed = 2,
    /// <summary>Not sent on purpose: the group is switched off, the device is gone, push is disabled, or it's stale.</summary>
    Skipped = 3
}
