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
    INovelsRepository novelsRepository) : IRequestHandler<GetPrivilegeInfoQuery, PrivilegeInfoDto?>
{
    public async Task<PrivilegeInfoDto?> Handle(GetPrivilegeInfoQuery request, CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting privilege info for novel {NovelId}", request.NovelId);
        
        var privilege = await privilegeService.GetPrivilegeConfigAsync(request.NovelId);
        if (privilege == null || !privilege.IsEnabled)
            return null;
        
        var currentUser = userContext.GetCurrentUser();
        var isSubscribed = false;
        DateTime? subscribedAt = null;
        
        if (currentUser != null)
        {
            var subscription = await privilegeService.HasActiveSubscriptionAsync(
                request.NovelId, 
                currentUser.Id);
            isSubscribed = subscription;
        }
        
        var totalPublished = await novelsRepository.GetPublishedChaptersCountAsync(request.NovelId);
        
        return new PrivilegeInfoDto
        {
            IsEnabled = privilege.IsEnabled,
            SubscriptionCost = privilege.SubscriptionCost,
            LockedChaptersCount = privilege.CurrentLockedCount,
            PrivilegeStartSequence = privilege.PrivilegeStartSequence,
            TotalPublishedChapters = totalPublished,
            IsSubscribed = isSubscribed,
            SubscribedAt = subscribedAt
        };
    }
}
