using Application.Notifications.DTOs;
using MediatR;

namespace Application.Notifications.Commands.UpdatePreferences;

/// <summary>Switches push notification groups on or off; a group left out (null) keeps its current setting.</summary>
public class UpdateNotificationPreferencesCommand : IRequest<NotificationPreferencesDto>
{
    public bool? Social { get; set; }
    public bool? Chapters { get; set; }
    public bool? Support { get; set; }
}
