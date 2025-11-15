using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Queries.GetUnreadCount;

public class GetUnreadCountQueryHandler(
    ILogger<GetUnreadCountQueryHandler> logger,
    INotificationsRepository notificationsRepository,
    IUserContext userContext) : IRequestHandler<GetUnreadCountQuery, int>
{
    public async Task<int> Handle(GetUnreadCountQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        logger.LogDebug("Getting unread notifications count for user {UserId}", currentUser.Id);

        var unreadCount = await notificationsRepository.GetUnreadCount(currentUser.Id);
        
        return unreadCount;
    }
}
