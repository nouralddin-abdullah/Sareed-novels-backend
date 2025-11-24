using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Privileges.Commands.UpdatePrivilege;

public class UpdatePrivilegeCommandHandler(
    ILogger<UpdatePrivilegeCommandHandler> logger,
    IUserContext userContext,
    IPrivilegeService privilegeService) : IRequestHandler<UpdatePrivilegeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdatePrivilegeCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating privilege config for novel {NovelId}: Cost={Cost}, NewStart={NewStart}", 
            request.NovelId, request.NewSubscriptionCost, request.NewPrivilegeStartSequence);
        
        var currentUser = userContext.GetCurrentUser() 
            ?? throw new ForbidException("User not signed in");
        
        var result = await privilegeService.UpdatePrivilegeConfigAsync(
            request.NovelId, 
            currentUser.Id, 
            request.NewSubscriptionCost,
            request.NewPrivilegeStartSequence);
        
        return result;
    }
}
