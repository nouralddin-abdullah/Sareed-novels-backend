using Application.Notifications.DTOs;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Notifications.Queries.GetPreferences;

public class GetNotificationPreferencesQueryHandler(
    INotificationPreferencesRepository preferencesRepository,
    IUserContext userContext) : IRequestHandler<GetNotificationPreferencesQuery, NotificationPreferencesDto>
{
    public async Task<NotificationPreferencesDto> Handle(GetNotificationPreferencesQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("يجب تسجيل الدخول أولًا", "NotSignedIn");
        return NotificationPreferencesDto.From(await preferencesRepository.Get(currentUser.Id));
    }
}
