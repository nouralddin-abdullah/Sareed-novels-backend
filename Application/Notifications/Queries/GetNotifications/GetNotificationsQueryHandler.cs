using Application.Notifications.DTOs;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Queries.GetNotifications;

public class GetNotificationsQueryHandler(
    ILogger<GetNotificationsQueryHandler> logger,
    INotificationsRepository notificationsRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetNotificationsQuery, NotificationListDto>
{
    public async Task<NotificationListDto> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        logger.LogInformation("Getting notifications for user {UserId}, page {PageNumber}, unreadOnly {UnreadOnly}", 
            currentUser.Id, request.PageNumber, request.UnreadOnly);

        var (notifications, totalCount) = await notificationsRepository.GetUserNotifications(
            currentUser.Id,
            request.PageNumber,
            request.PageSize,
            request.UnreadOnly);

        var notificationDtos = mapper.Map<List<NotificationDto>>(notifications);
        
        var unreadCount = await notificationsRepository.GetUnreadCount(currentUser.Id);
        
        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return new NotificationListDto
        {
            Notifications = notificationDtos,
            TotalCount = totalCount,
            UnreadCount = unreadCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalPages = totalPages
        };
    }
}
