namespace Domain.Entities;

/// <summary>
/// A phone that receives push notifications for <see cref="UserId"/>: its Firebase Cloud Messaging registration
/// token. One row per token; a token signed into by another user moves to that user.
/// </summary>
public class UserDevice
{
    public const int TokenMaxLength = 512;

    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public string Token { get; set; } = default!;
    /// <summary>One of <see cref="Constants.DevicePlatforms"/>.</summary>
    public string Platform { get; set; } = default!;
    public string? AppVersion { get; set; }
    public string Locale { get; set; } = "ar";
    public DateTime CreatedAt { get; set; }
    /// <summary>Last time the app registered this token (after sign-in, on token refresh).</summary>
    public DateTime LastSeenAt { get; set; }
}
