using Application.Notifications.DTOs;
using MediatR;

namespace Application.Notifications.Queries.GetPreferences;

public class GetNotificationPreferencesQuery : IRequest<NotificationPreferencesDto>
{
}
