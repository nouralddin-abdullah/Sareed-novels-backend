using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Privileges.Commands.CancelSubscription;

public class CancelSubscriptionCommandHandler(
    ILogger<CancelSubscriptionCommandHandler> logger,
    IUserContext userContext,
    IPrivilegeService privilegeService) : IRequestHandler<CancelSubscriptionCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CancelSubscriptionCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("User cancelling privilege subscription for novel {NovelId}", request.NovelId);
        
        var currentUser = userContext.GetCurrentUser() 
            ?? throw new ForbidException("User not signed in");
        
        var result = await privilegeService.CancelSubscriptionAsync(
            request.NovelId, 
            currentUser.Id);
        
        return result;
    }
}
