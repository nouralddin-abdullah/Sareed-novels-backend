using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Privileges.Commands.EnablePrivilege;

public class EnablePrivilegeCommandHandler(
    ILogger<EnablePrivilegeCommandHandler> logger,
    IUserContext userContext,
    IPrivilegeService privilegeService) : IRequestHandler<EnablePrivilegeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(EnablePrivilegeCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Enabling privilege for novel {NovelId} with cost {Cost}, start sequence: {StartSeq}", 
            request.NovelId, request.SubscriptionCost, request.PrivilegeStartSequence);
        
        var currentUser = userContext.GetCurrentUser() 
            ?? throw new ForbidException("User not signed in");
        
        var result = await privilegeService.EnablePrivilegeAsync(
            request.NovelId, 
            currentUser.Id, 
            request.SubscriptionCost,
            request.PrivilegeStartSequence);
        
        return result;
    }
}
