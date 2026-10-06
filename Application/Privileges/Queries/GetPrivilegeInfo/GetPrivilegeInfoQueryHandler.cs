using Application.Privileges.DTOs;
using Application.Services;
using Application.Users;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Privileges.Queries.GetPrivilegeInfo;

/// <summary>A novel's early access (#94): its settings, what its chapters' locks come to now, and the viewer's subscription.</summary>
public class GetPrivilegeInfoQueryHandler(
    ILogger<GetPrivilegeInfoQueryHandler> logger,
    IUserContext userContext,
    IPrivilegeService privilegeService,
    IPrivilegeSubscriptionRepository subscriptionRepository) : IRequestHandler<GetPrivilegeInfoQuery, PrivilegeInfoDto?>
{
    public async Task<PrivilegeInfoDto?> Handle(GetPrivilegeInfoQuery request, CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting privilege info for novel {NovelId}", request.NovelId);

        var privilege = await privilegeService.GetPrivilegeConfigAsync(request.NovelId);
        if (privilege is not { IsEnabled: true })
            return null;

        var summary = await privilegeService.SummarizeAsync(request.NovelId, EarlyAccessSettings.Of(privilege));

        // The signed-in reader's active subscription, and when it began; how many subscribed, for the author only.
        var currentUser = userContext.GetCurrentUser();
        var subscribedAt = currentUser == null
            ? null
            : await subscriptionRepository.GetActiveSubscriptionDateAsync(request.NovelId, currentUser.Id);
        var isAuthor = currentUser != null && privilege.Novel?.AuthorId == currentUser.Id;

        return new PrivilegeInfoDto
        {
            IsEnabled = true,
            SubscriptionCost = privilege.SubscriptionCost,
            EarlyAccessDays = privilege.SubscribersOnly ? null : privilege.EarlyAccessDays,
            SubscribersOnly = privilege.SubscribersOnly,
            LockedChaptersCount = summary.LockedCount,
            NextUnlockAt = summary.NextUnlockAt,
            PrivilegeStartSequence = summary.FirstLockedSequence ?? summary.PublishedCount + 1,
            TotalPublishedChapters = summary.PublishedCount,
            SubscribersCount = isAuthor ? await subscriptionRepository.CountActiveSubscribersAsync(request.NovelId) : null,
            IsSubscribed = subscribedAt.HasValue,
            SubscribedAt = subscribedAt
        };
    }
}
