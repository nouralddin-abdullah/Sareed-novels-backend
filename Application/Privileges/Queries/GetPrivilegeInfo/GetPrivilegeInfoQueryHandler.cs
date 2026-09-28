using Application.Privileges.DTOs;
using Application.Services;
using Application.Users;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Privileges.Queries.GetPrivilegeInfo;

public class GetPrivilegeInfoQueryHandler(
    ILogger<GetPrivilegeInfoQueryHandler> logger,
    IUserContext userContext,
    IPrivilegeService privilegeService,
    IPrivilegeSubscriptionRepository subscriptionRepository,
    INovelsRepository novelsRepository) : IRequestHandler<GetPrivilegeInfoQuery, PrivilegeInfoDto?>
{
    public async Task<PrivilegeInfoDto?> Handle(GetPrivilegeInfoQuery request, CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting privilege info for novel {NovelId}", request.NovelId);
        
        var privilege = await privilegeService.GetPrivilegeConfigAsync(request.NovelId);
        if (privilege == null || !privilege.IsEnabled)
            return null;
        
        // The signed-in reader's active subscription, and when it began (this was always null).
        var currentUser = userContext.GetCurrentUser();
        var subscribedAt = currentUser == null
            ? null
            : await subscriptionRepository.GetActiveSubscriptionDateAsync(request.NovelId, currentUser.Id);
        
        var totalPublished = await novelsRepository.GetPublishedChaptersCountAsync(request.NovelId);
        
        return new PrivilegeInfoDto
        {
            IsEnabled = privilege.IsEnabled,
            SubscriptionCost = privilege.SubscriptionCost,
            LockedChaptersCount = privilege.CurrentLockedCount,
            PrivilegeStartSequence = privilege.PrivilegeStartSequence,
            TotalPublishedChapters = totalPublished,
            IsSubscribed = subscribedAt.HasValue,
            SubscribedAt = subscribedAt
        };
    }
}
