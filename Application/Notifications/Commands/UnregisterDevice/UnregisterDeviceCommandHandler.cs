using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Notifications.Commands.UnregisterDevice;

public class UnregisterDeviceCommandHandler(
    IUserDevicesRepository devicesRepository,
    IUserContext userContext) : IRequestHandler<UnregisterDeviceCommand>
{
    public async Task Handle(UnregisterDeviceCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("يجب تسجيل الدخول أولًا");

        // Someone else's token (or one that's already gone) is left alone without saying so.
        await devicesRepository.Remove(currentUser.Id, request.Token.Trim());
    }
}
