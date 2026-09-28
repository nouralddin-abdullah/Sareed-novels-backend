using MediatR;

namespace Application.Notifications.Commands.MarkAllAsRead;

/// <summary>Marks every unread notification of the caller read; answers how many there were (0 included).</summary>
public class MarkAllNotificationsAsReadCommand : IRequest<int>
{
}
