using System.Text.RegularExpressions;
using Application.Notifications.DTOs;
using Application.Users;
using AutoMapper;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Application.Common;

namespace Application.Notifications.Queries.GetNotifications;

public partial class GetNotificationsQueryHandler(
    ILogger<GetNotificationsQueryHandler> logger,
    INotificationsRepository notificationsRepository,
    INovelsRepository novelsRepository,
    IReviewsRepository reviewsRepository,
    IChaptersRepository chaptersRepository,
    IReadingListsRepository readingListsRepository,
    IGiftRepository giftRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetNotificationsQuery, NotificationListDto>
{
    public async Task<NotificationListDto> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        
        logger.LogInformation("Getting notifications for user {UserId}, page {PageNumber}, unreadOnly {UnreadOnly}", 
            currentUser.Id, pageNumber, request.UnreadOnly);

        var (notifications, totalCount) = await notificationsRepository.GetUserNotifications(
            currentUser.Id,
            pageNumber,
            pageSize,
            request.UnreadOnly);

        var notificationDtos = mapper.Map<List<NotificationDto>>(notifications);
        await SetParts(notificationDtos);
        
        var unreadCount = await notificationsRepository.GetUnreadCount(currentUser.Id);
        
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new NotificationListDto
        {
            Notifications = notificationDtos,
            TotalCount = totalCount,
            UnreadCount = unreadCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = totalPages
        };
    }

    /// <summary>
    /// Sets what each notification is about, and the current names its message quotes, in a few small lookups for the
    /// page. Gift and privilege notifications keep the novel's id in RelatedEntityId, new-chapter ones in ActorId, and
    /// review ones reach it through their review. A new chapter's id is its RelatedEntityId; a comment on a chapter
    /// links to the chapter (ActionUrl), whatever became of the comment.
    /// </summary>
    private async Task SetParts(List<NotificationDto> notifications)
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
            notification.ChapterId = notification.Type switch
            {
                NotificationType.NewChapterInLibrary => notification.RelatedEntityId,
                NotificationType.CommentOnChapter => ChapterIdIn(notification.ActionUrl),
                _ => null
            };
        }

        var novels = await novelsRepository.GetSlugsAndTitlesAsync(Distinct(notifications.Select(n => n.NovelId)));
        var chapterTitles = await chaptersRepository.GetTitlesAsync(Distinct(notifications.Select(n => n.ChapterId)));
        var listNames = await readingListsRepository.GetNamesAsync(Distinct(notifications
            .Where(n => n.Type == NotificationType.ReadingListFollowed)
            .Select(n => n.RelatedEntityId)));
        var giftNames = await giftRepository.GetArabicNamesAsync(Distinct(notifications.Select(n => n.GiftId)));

        foreach (var notification in notifications)
        {
            if (notification.NovelId is { } novelId && novels.TryGetValue(novelId, out var novel))
            {
                (notification.NovelSlug, notification.NovelTitle) = novel;
            }
            notification.ChapterTitle = notification.ChapterId is { } chapterId ? chapterTitles.GetValueOrDefault(chapterId) : null;
            notification.ReadingListName = notification.Type == NotificationType.ReadingListFollowed
                && notification.RelatedEntityId is { } listId ? listNames.GetValueOrDefault(listId) : null;
            notification.GiftNameAr = notification.GiftId is { } giftId ? giftNames.GetValueOrDefault(giftId) : null;
        }
    }

    private static List<Guid> Distinct(IEnumerable<Guid?> ids) => ids.OfType<Guid>().Distinct().ToList();

    /// <summary>The chapter of a link NotificationService writes as /novel/{slug}/chapter/{chapterId}.</summary>
    public static Guid? ChapterIdIn(string? actionUrl) =>
        actionUrl != null && ChapterLink().Match(actionUrl) is { Success: true } match ? Guid.Parse(match.Groups[1].Value) : null;

    [GeneratedRegex(@"/chapter/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$")]
    private static partial Regex ChapterLink();
}
