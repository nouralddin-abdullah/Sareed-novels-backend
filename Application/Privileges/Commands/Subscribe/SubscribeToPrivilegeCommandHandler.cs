using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Privileges.Commands.Subscribe;

public class SubscribeToPrivilegeCommandHandler(
    ILogger<SubscribeToPrivilegeCommandHandler> logger,
    IUserContext userContext,
    IPrivilegeService privilegeService) : IRequestHandler<SubscribeToPrivilegeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(SubscribeToPrivilegeCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("User subscribing to privilege for novel {NovelId}", request.NovelId);
        
        var currentUser = userContext.GetCurrentUser() 
            ?? throw new ForbidException("User not signed in");
        
        var result = await privilegeService.SubscribeToPrivilegeAsync(
            request.NovelId, 
            currentUser.Id);
        
        return result;
    }
}
