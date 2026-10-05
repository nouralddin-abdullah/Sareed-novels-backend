using MediatR;

namespace Application.Notifications.Queries.GetUnreadCount;

/// <param name="types">The types filter as sent: comma-separated NotificationType names (<see cref="NotificationTypeFilter"/>).</param>
public class GetUnreadCountQuery(IReadOnlyList<string>? types = null) : IRequest<int>
{
    public IReadOnlyList<string>? Types { get; set; } = types;
}
