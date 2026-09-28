using Application.Services;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.UnFollowUser;

public class UnFollowUserCommandHandler(
    ILogger<UnFollowUserCommandHandler> logger, 
    IUserContext userContext, 
    UserManager<User> userManager, 
    IUsersRepository usersRepository) : IRequestHandler<UnFollowUserCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UnFollowUserCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("User {UserId} trying to unfollow {UserId}", currentUser.Id, request.UserToUnFollowId);
        var userToUnFollow = await userManager.FindByIdAsync(request.UserToUnFollowId) ?? throw new NotFoundException("المستخدم غير موجود", "UserNotFound");

        if (currentUser.Id == userToUnFollow.Id)
        {
            return new OperationResult
            {
                Success = false,
                Code = "CannotUnfollowSelf",
                Message = "لا يمكنك إلغاء متابعة نفسك",
            };
        }
        // Not following, or a concurrent unfollow got there first: the same answer.
        if (!await usersRepository.UnFollowUser(currentUser.Id, userToUnFollow.Id))
        {
            return new OperationResult
            {
                Success = false,
                Code = "NotFollowing",
                Message = "أنت لا تتابع هذا المستخدم",
            };
        }

        return new OperationResult
        {
            Success = true,
            Message = $"ألغيت متابعة {userToUnFollow.DisplayName}"
        };
    }
}
