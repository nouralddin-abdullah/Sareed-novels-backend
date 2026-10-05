using Application.Notifications.DTOs;
using MediatR;

namespace Application.Notifications.Queries.GetNotifications;

/// <param name="types">The types filter as sent: comma-separated NotificationType names (<see cref="NotificationTypeFilter"/>).</param>
public class GetNotificationsQuery(int pageNumber = 1, int pageSize = 20, bool unreadOnly = false, IReadOnlyList<string>? types = null)
    : IRequest<NotificationListDto>
{
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
    public bool UnreadOnly { get; set; } = unreadOnly;
    public IReadOnlyList<string>? Types { get; set; } = types;
}
