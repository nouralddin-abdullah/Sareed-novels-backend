using Application.Notifications.DTOs;
using Application.Users;
using AutoMapper;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Queries.GetNotifications;

public class GetNotificationsQueryHandler(
    ILogger<GetNotificationsQueryHandler> logger,
    INotificationsRepository notificationsRepository,
    INovelsRepository novelsRepository,
    IReviewsRepository reviewsRepository,
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
        await SetNovels(notificationDtos);
        
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

    /// <summary>
    /// Sets the novel each notification is about, and that novel's current slug, in two small lookups for the page.
    /// Gift and privilege notifications keep the novel's id in RelatedEntityId, new-chapter ones in ActorId, and review
    /// ones reach it through their review.
    /// </summary>
    private async Task SetNovels(List<NotificationDto> notifications)
    {
        var reviewIds = notifications
            .Where(n => n.Type is NotificationType.ReviewOnNovel or NotificationType.LikeOnReview)
            .Select(n => n.RelatedEntityId)
            .OfType<Guid>()
            .ToList();
        var reviewNovelIds = reviewIds.Count == 0 ? [] : await reviewsRepository.GetNovelIdsAsync(reviewIds);

        foreach (var notification in notifications)
        {
            notification.NovelId = notification.Type switch
            {
                NotificationType.GiftReceived or NotificationType.PrivilegeSubscribed => notification.RelatedEntityId,
                NotificationType.NewChapterInLibrary => Guid.TryParse(notification.ActorId, out var novelId) ? novelId : null,
                NotificationType.ReviewOnNovel or NotificationType.LikeOnReview
                    when notification.RelatedEntityId is { } reviewId && reviewNovelIds.TryGetValue(reviewId, out var reviewedNovelId)
                    => reviewedNovelId,
                _ => null
            };
        }

        var novelIds = notifications.Select(n => n.NovelId).OfType<Guid>().Distinct().ToList();
        if (novelIds.Count == 0)
        {
            return;
        }

        var slugs = await novelsRepository.GetSlugsAsync(novelIds);
        foreach (var notification in notifications)
        {
            notification.NovelSlug = notification.NovelId is { } id ? slugs.GetValueOrDefault(id) : null;
        }
    }
}
