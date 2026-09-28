using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.ChangePassword;

public class ChangePasswordCommandHandler(ILogger<ChangePasswordCommandHandler> logger, UserManager<User> userManager,IUserContext userContext ) : IRequestHandler<ChangePasswordCommand, IdentityResult>
{
    public async Task<IdentityResult> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("Updating password for user {UserId}", currentUser.Id);
        var user = await userManager.FindByIdAsync(currentUser.Id) ?? throw new NotFoundException("المستخدم غير موجود", "UserNotFound");
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result;
    }
}
