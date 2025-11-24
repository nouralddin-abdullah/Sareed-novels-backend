using Application.Privileges.DTOs;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Privileges.Queries.GetMySubscriptions;

public class GetMySubscriptionsQueryHandler(
    ILogger<GetMySubscriptionsQueryHandler> logger,
    IUserContext userContext,
    IPrivilegeSubscriptionRepository subscriptionRepository) 
    : IRequestHandler<GetMySubscriptionsQuery, (List<PrivilegeSubscriptionDto>, int)>
{
    public async Task<(List<PrivilegeSubscriptionDto>, int)> Handle(
        GetMySubscriptionsQuery request, 
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Getting subscriptions for current user");
        
        var currentUser = userContext.GetCurrentUser() 
            ?? throw new ForbidException("User not signed in");
        
        var (subscriptions, totalCount) = await subscriptionRepository.GetUserSubscriptionsAsync(
            currentUser.Id, 
            request.PageNumber, 
            request.PageSize,
            includeExpired: false);
        
        var dtos = subscriptions.Select(s => new PrivilegeSubscriptionDto
        {
            Id = s.Id,
            NovelId = s.NovelId,
            NovelTitle = s.Novel?.Title ?? "Unknown",
            NovelCoverImageUrl = s.Novel?.CoverImageUrl,
            IsActive = s.IsActive,
            SubscribedAt = s.SubscribedAt,
            AmountPaid = s.AmountPaid
        }).ToList();
        
        return (dtos, totalCount);
    }
}
