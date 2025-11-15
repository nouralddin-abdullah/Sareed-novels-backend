using MediatR;

namespace Application.Notifications.Commands.MarkAsRead;

public class MarkNotificationAsReadCommand(Guid notificationId) : IRequest<bool>
{
    public Guid NotificationId { get; set; } = notificationId;
}
