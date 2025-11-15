using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Commands.MarkAllAsRead;

public class MarkAllNotificationsAsReadCommandHandler(
    ILogger<MarkAllNotificationsAsReadCommandHandler> logger,
    INotificationsRepository notificationsRepository,
    IUserContext userContext) : IRequestHandler<MarkAllNotificationsAsReadCommand, bool>
{
    public async Task<bool> Handle(MarkAllNotificationsAsReadCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        logger.LogInformation("Marking all notifications as read for user {UserId}", currentUser.Id);

        var result = await notificationsRepository.MarkAllAsRead(currentUser.Id);
        
        if (result)
        {
            logger.LogDebug("All notifications marked as read for user {UserId}", currentUser.Id);
        }

        return result;
    }
}
