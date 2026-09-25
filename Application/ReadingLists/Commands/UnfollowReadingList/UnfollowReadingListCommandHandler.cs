using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Commands.UnfollowReadingList;

public class UnfollowReadingListCommandHandler(
    ILogger<UnfollowReadingListCommandHandler> logger,
    IReadingListsRepository readingListsRepository,
    IReadingListFollowersRepository followersRepository,
    IUserContext userContext) : IRequestHandler<UnfollowReadingListCommand, OperationResult>
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
            await readingListsRepository.AdjustFollowersCountAsync(request.ReadingListId, -1);

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
}
