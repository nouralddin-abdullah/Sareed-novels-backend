using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.FollowUser;

public class FollowUserCommandHandler(
    ILogger<FollowUserCommandHandler> logger, 
    IUserContext userContext, 
    UserManager<User> userManager, 
    IUsersRepository usersRepository,
    IUserBlocksRepository blocksRepository,
    IServiceProvider serviceProvider) : IRequestHandler<FollowUserCommand, OperationResult>
{
    public async Task<OperationResult> Handle(FollowUserCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("User {UserId} trying to follow {UserId}", currentUser.Id, request.UserIdToFollow);
        var userToFollow = await userManager.FindByIdAsync(request.UserIdToFollow) ?? throw new NotFoundException("المستخدم غير موجود", "UserNotFound");

        if (currentUser.Id == userToFollow.Id)
        {
            return new OperationResult
            {
                Success = false,
                Code = "CannotFollowSelf",
                Message = "لا يمكنك متابعة نفسك",
            };
        }
        // Blocking removed any follow between the two, and neither can follow the other while it lasts.
        var blocks = await blocksRepository.GetRelationAsync(currentUser.Id, userToFollow.Id, cancellationToken);
        if (blocks.OtherBlockedViewer)
        {
            throw new ForbidException("لا يمكنك متابعة هذا المستخدم", "Blocked");
        }
        if (blocks.ViewerBlockedOther)
        {
            throw new ForbidException("ألغِ حظر هذا المستخدم أولاً لتتمكن من متابعته", "Blocked");
        }

        // Already following, or a concurrent follow (a double tap) got there first: the same answer.
        if (await usersRepository.IsFollowingAsync(currentUser.Id, userToFollow.Id)
            || !await usersRepository.FollowUser(currentUser.Id, userToFollow.Id))
        {
            return OperationResult.AlreadyDone("AlreadyFollowing", "أنت تتابع هذا المستخدم بالفعل");
        }

        // Fire-and-forget: Send notification
        _ = SendNewFollowerNotificationInBackground(userToFollow.Id, currentUser.Id);

        return new OperationResult
        {
            Success = true,
            Message = $"أنت تتابع {userToFollow.DisplayName} الآن"
        };
    }
    
    private async Task SendNewFollowerNotificationInBackground(string targetUserId, string actorUserId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var backgroundNotificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            
            var followerUser = await backgroundUserManager.FindByIdAsync(actorUserId);
            if (followerUser != null)
            {
                await backgroundNotificationService.SendNewFollowerNotification(targetUserId, followerUser);
                logger.LogDebug("Sent NewFollower notification to user {UserId}", targetUserId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send NewFollower notification in background for user {UserId}", targetUserId);
        }
    }
}
