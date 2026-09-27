using MediatR;

namespace Application.Notifications.Commands.RegisterDevice;

/// <summary>
/// The mobile app registers its Firebase Cloud Messaging token for the signed-in user (after sign-in and whenever
/// FCM gives it a new token). Registering a token again, or from another account, updates or moves it.
/// </summary>
public class RegisterDeviceCommand : IRequest
{
    public string Token { get; set; } = default!;

    /// <summary>"android" or "ios".</summary>
    public string Platform { get; set; } = default!;

    public string? AppVersion { get; set; }

    /// <summary>Defaults to "ar" (Sard is Arabic-only).</summary>
    public string? Locale { get; set; }
}
