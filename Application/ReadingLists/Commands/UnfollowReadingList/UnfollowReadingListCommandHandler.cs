using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Commands.UnfollowReadingList;

public class UnfollowReadingListCommandHandler(
    ILogger<UnfollowReadingListCommandHandler> logger,
    IReadingListsRepository readingListsRepository,
    IReadingListFollowersRepository followersRepository,
    IUserContext userContext,
    IServiceProvider serviceProvider) : IRequestHandler<UnfollowReadingListCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UnfollowReadingListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        logger.LogInformation("User {UserId} trying to unfollow reading list {ListId}", currentUser.Id, request.ReadingListId);

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("Reading list not found");

        if (readingList.UserId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You cannot unfollow your own reading list"
            };
        }

        var isFollowing = await followersRepository.IsFollowingAsync(request.ReadingListId, currentUser.Id);

        if (!isFollowing)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You are not following this reading list"
            };
        }

        var result = await followersRepository.UnfollowAsync(request.ReadingListId, currentUser.Id);

        if (result)
        {
            // Fire-and-forget count update
            _ = UpdateFollowersCountInBackground(request.ReadingListId);

            logger.LogInformation("User {UserId} successfully unfollowed reading list {ListId}", currentUser.Id, request.ReadingListId);

            return new OperationResult
            {
                Success = true,
                Message = $"Successfully unfollowed '{readingList.Name}'"
            };
        }

        return new OperationResult
        {
            Success = false,
            Message = "Failed to unfollow reading list"
        };
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
                list.DecrementFollowersCount();
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
