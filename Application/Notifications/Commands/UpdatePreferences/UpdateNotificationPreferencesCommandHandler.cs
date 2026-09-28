using Application.Notifications.DTOs;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Commands.UpdatePreferences;

public class UpdateNotificationPreferencesCommandHandler(
    ILogger<UpdateNotificationPreferencesCommandHandler> logger,
    INotificationPreferencesRepository preferencesRepository,
    IUserContext userContext) : IRequestHandler<UpdateNotificationPreferencesCommand, NotificationPreferencesDto>
{
    public async Task<NotificationPreferencesDto> Handle(UpdateNotificationPreferencesCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("يجب تسجيل الدخول أولًا", "NotSignedIn");

        var preferences = await preferencesRepository.Update(currentUser.Id, request.Social, request.Chapters, request.Support);
        logger.LogInformation("User {UserId} set push notifications: social {Social}, chapters {Chapters}, support {Support}",
            currentUser.Id, preferences.Social, preferences.Chapters, preferences.Support);

        return NotificationPreferencesDto.From(preferences);
    }
}
