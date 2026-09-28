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
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("User {UserId} trying to follow reading list {ListId}", currentUser.Id, request.ReadingListId);

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("القائمة غير موجودة", "ReadingListNotFound");

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

        var isFollowing = await followersRepository.IsFollowingAsync(request.ReadingListId, currentUser.Id);

        if (isFollowing)
        {
            return new OperationResult
            {
                Success = false,
                Code = "AlreadyFollowing",
                Message = "أنت تتابع هذه القائمة بالفعل"
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

        return new OperationResult
        {
            Success = false,
            Code = "OperationFailed",
            Message = "تعذّرت متابعة القائمة. حاول مرة أخرى."
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
