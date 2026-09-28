using MediatR;

namespace Application.Notifications.Commands.UnregisterDevice;

/// <summary>
/// The app unregisters its FCM token on sign-out, also when it has no valid session any more (signed out by a 401,
/// or offline and sent later). Signed in, it removes only the caller's own registration of the token; without a
/// session (no token, or an expired or revoked one) it removes the token whoever registered it, since the FCM token
/// is a secret only that phone knows. Idempotent, and the answer never says whether the token was registered.
/// </summary>
public class UnregisterDeviceCommand(string token) : IRequest
{
    public string Token { get; } = token;
}
