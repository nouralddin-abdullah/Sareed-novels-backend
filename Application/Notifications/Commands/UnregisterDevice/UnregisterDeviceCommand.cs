using MediatR;

namespace Application.Notifications.Commands.UnregisterDevice;

/// <summary>The app unregisters its token on sign-out. Only removes the caller's own token; idempotent.</summary>
public class UnregisterDeviceCommand(string token) : IRequest
{
    public string Token { get; } = token;
}
