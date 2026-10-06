using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Privileges.Commands.ManualUnlock;

public class ManualUnlockChapterCommandHandler(
    ILogger<ManualUnlockChapterCommandHandler> logger,
    IUserContext userContext,
    IPrivilegeService privilegeService) : IRequestHandler<ManualUnlockChapterCommand, OperationResult>
{
    public async Task<OperationResult> Handle(ManualUnlockChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Manually unlocking chapter {ChapterId}", request.ChapterId);
        
        var currentUser = userContext.GetCurrentUser() 
            ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        
        var result = await privilegeService.ManuallyUnlockChapterAsync(request.NovelId, request.ChapterId, currentUser.Id);
        
        return result;
    }
}
