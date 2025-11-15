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
    ISearchIndexQueueService searchIndexQueue,
    IServiceProvider serviceProvider) : IRequestHandler<FollowUserCommand, OperationResult>
{
    public async Task<OperationResult> Handle(FollowUserCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("The user is not authorized");
        logger.LogInformation("User {UserId} trying to follow {UserId}", currentUser.Id, request.UserIdToFollow);
        var userToFollow = await userManager.FindByIdAsync(request.UserIdToFollow) ?? throw new NotFoundException("User you trying to follow not found");

        if (currentUser.Id == userToFollow.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You cannot follow yourself",
            };
        }
        var isFollowing = await usersRepository.IsFollowingAsync(currentUser.Id, userToFollow.Id);

        if (isFollowing)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You already following this user",
            };
        }

        var result = await usersRepository.FollowUser(currentUser.Id, userToFollow.Id);
        
        if (result)
        {
            // Fire-and-forget: Send notification
            _ = SendNewFollowerNotificationInBackground(userToFollow.Id, currentUser.Id);
            
            // Update both users in search index (follower count changed)
            await searchIndexQueue.QueueUserUpdateAsync(currentUser.Id);
            await searchIndexQueue.QueueUserUpdateAsync(userToFollow.Id);
            logger.LogDebug("Queued users for search index update after follow");
        }

        var message = result ? $"Successfully followed {userToFollow.DisplayName}" : "Failed to follow user";
        return new OperationResult
        {
            Success = result,
            Message = message
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
