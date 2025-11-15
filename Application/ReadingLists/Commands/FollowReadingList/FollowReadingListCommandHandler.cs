using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Commands.FollowReadingList;

public class FollowReadingListCommandHandler(
    ILogger<FollowReadingListCommandHandler> logger,
    IReadingListsRepository readingListsRepository,
    IReadingListFollowersRepository followersRepository,
    IUserContext userContext,
    IServiceProvider serviceProvider) : IRequestHandler<FollowReadingListCommand, OperationResult>
{
    public async Task<OperationResult> Handle(FollowReadingListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        logger.LogInformation("User {UserId} trying to follow reading list {ListId}", currentUser.Id, request.ReadingListId);

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("Reading list not found");

        if (!readingList.IsPublic)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Cannot follow a private reading list"
            };
        }

        if (readingList.UserId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You cannot follow your own reading list"
            };
        }

        var isFollowing = await followersRepository.IsFollowingAsync(request.ReadingListId, currentUser.Id);

        if (isFollowing)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You are already following this reading list"
            };
        }

        var follower = new ReadingListFollower
        {
            ReadingListId = request.ReadingListId,
            UserId = currentUser.Id,
            FollowedAt = DateTime.UtcNow
        };

        var result = await followersRepository.FollowAsync(follower);

        if (result)
        {
            // Fire-and-forget count update
            _ = UpdateFollowersCountInBackground(request.ReadingListId);
            
            // Fire-and-forget: Send notification
            _ = SendReadingListFollowedNotificationInBackground(readingList.UserId, currentUser.Id, request.ReadingListId, readingList.Name);

            logger.LogInformation("User {UserId} successfully followed reading list {ListId}", currentUser.Id, request.ReadingListId);

            return new OperationResult
            {
                Success = true,
                Message = $"Successfully followed '{readingList.Name}'"
            };
        }

        return new OperationResult
        {
            Success = false,
            Message = "Failed to follow reading list"
        };
    }
    
    private async Task SendReadingListFollowedNotificationInBackground(string listOwnerId, string followerUserId, Guid readingListId, string readingListName)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
            var backgroundNotificationService = scope.ServiceProvider.GetRequiredService<Application.Services.INotificationService>();
            
            var follower = await backgroundUserManager.FindByIdAsync(followerUserId);
            if (follower != null)
            {
                await backgroundNotificationService.SendReadingListFollowedNotification(listOwnerId, follower, readingListId, readingListName);
                logger.LogDebug("Sent ReadingListFollowed notification to user {UserId}", listOwnerId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send ReadingListFollowed notification");
        }
    }

    private async Task UpdateFollowersCountInBackground(Guid readingListId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundRepository = scope.ServiceProvider.GetRequiredService<IReadingListsRepository>();

            var list = await backgroundRepository.GetByIdAsync(readingListId);
            if (list != null)
            {
                list.IncrementFollowersCount();
                await backgroundRepository.UpdateAsync(list);
            }

            logger.LogDebug("Updated followers count for reading list {ListId}", readingListId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update followers count for reading list {ListId}", readingListId);
        }
    }
}
