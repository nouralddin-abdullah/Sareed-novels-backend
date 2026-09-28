using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Commands.MarkAsRead;

public class MarkNotificationAsReadCommandHandler(
    ILogger<MarkNotificationAsReadCommandHandler> logger,
    INotificationsRepository notificationsRepository,
    IUserContext userContext) : IRequestHandler<MarkNotificationAsReadCommand, bool>
{
    public async Task<bool> Handle(MarkNotificationAsReadCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        
        logger.LogInformation("Marking notification {NotificationId} as read for user {UserId}", 
            request.NotificationId, currentUser.Id);

        var notification = await notificationsRepository.GetNotificationById(request.NotificationId)
            ?? throw new NotFoundException("الإشعار غير موجود", "NotificationNotFound");

        // Verify notification belongs to current user
        if (notification.UserId != currentUser.Id)
        {
            throw new ForbidException("لا يمكنك تحديد إشعارات غيرك كمقروءة", "NotOwner");
        }

        if (notification.IsRead)
        {
            return true; // Already read
        }

        // Deleted in the meantime: as missing as if it never existed.
        if (!await notificationsRepository.MarkAsRead(request.NotificationId))
        {
            throw new NotFoundException("الإشعار غير موجود", "NotificationNotFound");
        }

        logger.LogDebug("Notification {NotificationId} marked as read", request.NotificationId);
        return true;
    }
}
