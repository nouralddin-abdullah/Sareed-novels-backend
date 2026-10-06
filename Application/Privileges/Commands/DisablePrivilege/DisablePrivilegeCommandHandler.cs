using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using MediatR;

namespace Application.Privileges.Commands.DisablePrivilege;

public class DisablePrivilegeCommandHandler(IUserContext userContext, IPrivilegeService privilegeService)
    : IRequestHandler<DisablePrivilegeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DisablePrivilegeCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        return await privilegeService.DisablePrivilegeAsync(request.NovelId, currentUser.Id);
    }
}
