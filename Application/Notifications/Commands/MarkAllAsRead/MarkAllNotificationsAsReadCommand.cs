using MediatR;

namespace Application.Notifications.Commands.MarkAllAsRead;

public class MarkAllNotificationsAsReadCommand : IRequest<bool>
{
}
