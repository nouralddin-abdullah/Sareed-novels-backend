using Application.Notifications.DTOs;
using MediatR;

namespace Application.Notifications.Queries.GetNotifications;

public class GetNotificationsQuery(int pageNumber = 1, int pageSize = 20, bool unreadOnly = false) : IRequest<NotificationListDto>
{
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
    public bool UnreadOnly { get; set; } = unreadOnly;
}
