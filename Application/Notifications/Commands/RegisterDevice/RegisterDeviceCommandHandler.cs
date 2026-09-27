using Application.Users;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Commands.RegisterDevice;

public class RegisterDeviceCommandHandler(
    ILogger<RegisterDeviceCommandHandler> logger,
    IUserDevicesRepository devicesRepository,
    IUserContext userContext) : IRequestHandler<RegisterDeviceCommand>
{
    public async Task Handle(RegisterDeviceCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("يجب تسجيل الدخول أولًا");

        var now = DateTime.UtcNow;
        var platform = request.Platform.Trim().ToLowerInvariant();
        await devicesRepository.Upsert(new UserDevice
        {
            UserId = currentUser.Id,
            Token = request.Token.Trim(),
            Platform = platform,
            AppVersion = string.IsNullOrWhiteSpace(request.AppVersion) ? null : request.AppVersion.Trim(),
            Locale = string.IsNullOrWhiteSpace(request.Locale) ? "ar" : request.Locale.Trim(),
            CreatedAt = now,
            LastSeenAt = now
        });

        logger.LogInformation("Registered a {Platform} push device for user {UserId}", platform, currentUser.Id);
    }
}
