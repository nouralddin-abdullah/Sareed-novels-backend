using Application.Users;
using Domain.Entities;
using Domain.Repositories;
using MediatR;

namespace Application.Notifications.Commands.UnregisterDevice;

public class UnregisterDeviceCommandHandler(
    IUserDevicesRepository devicesRepository,
    IUserContext userContext) : IRequestHandler<UnregisterDeviceCommand>
{
    public async Task Handle(UnregisterDeviceCommand request, CancellationToken cancellationToken)
    {
        var token = request.Token.Trim();
        if (token.Length == 0 || token.Length > UserDevice.TokenMaxLength)
        {
            return; // never registered, since registering refuses such a token
        }

        if (userContext.GetCurrentUser() is { } currentUser)
        {
            // Someone else's token (or one that's already gone) is left alone without saying so.
            await devicesRepository.Remove(currentUser.Id, token);
            return;
        }

        // No valid session: the phone signed out after a 401 (an expired or revoked token) or while offline. The
        // token itself is the proof, and removing it only stops pushes to that phone.
        await devicesRepository.RemoveToken(token);
    }
}
