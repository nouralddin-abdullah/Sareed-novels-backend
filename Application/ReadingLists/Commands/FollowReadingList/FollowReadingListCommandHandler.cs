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
    IUserBlocksRepository blocksRepository,
    IUserContext userContext,
    IServiceProvider serviceProvider) : IRequestHandler<FollowReadingListCommand, OperationResult>
{
    public async Task<OperationResult> Handle(FollowReadingListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("User {UserId} trying to follow reading list {ListId}", currentUser.Id, request.ReadingListId);

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException(ReadingListBlocks.NotFoundMessage, ReadingListBlocks.NotFoundCode);

        // A list whose owner blocked the caller doesn't exist for them (and following it would notify the owner).
        await ReadingListBlocks.EnsureNotBlockedByOwnerAsync(blocksRepository, readingList.UserId, currentUser.Id, cancellationToken);

        if (!readingList.IsPublic)
        {
            return new OperationResult
            {
                Success = false,
                Code = "ReadingListPrivate",
                Message = "هذه القائمة خاصة"
            };
        }

        if (readingList.UserId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Code = "CannotFollowOwnList",
                Message = "هذه قائمتك، فلا يمكنك متابعتها"
            };
        }

        var follower = new ReadingListFollower
        {
            ReadingListId = request.ReadingListId,
            UserId = currentUser.Id,
            FollowedAt = DateTime.UtcNow
        };

        // Already following, or a concurrent follow (a double tap) got there first: the same answer.
        if (await followersRepository.IsFollowingAsync(request.ReadingListId, currentUser.Id)
            || !await followersRepository.FollowAsync(follower))
        {
            return OperationResult.AlreadyDone("AlreadyFollowing", "أنت تتابع هذه القائمة بالفعل");
        }

        await readingListsRepository.AdjustFollowersCountAsync(request.ReadingListId, +1);

        // Fire-and-forget: Send notification
        _ = SendReadingListFollowedNotificationInBackground(readingList.UserId, currentUser.Id, request.ReadingListId, readingList.Name);

        logger.LogInformation("User {UserId} successfully followed reading list {ListId}", currentUser.Id, request.ReadingListId);

        return new OperationResult
        {
            Success = true,
            Message = $"أنت تتابع «{readingList.Name}» الآن"
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
}
