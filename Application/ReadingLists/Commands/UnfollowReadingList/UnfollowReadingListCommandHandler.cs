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
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("User {UserId} trying to unfollow reading list {ListId}", currentUser.Id, request.ReadingListId);

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("القائمة غير موجودة", "ReadingListNotFound");

        if (readingList.UserId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Code = "CannotUnfollowOwnList",
                Message = "هذه قائمتك، فلا يمكنك إلغاء متابعتها"
            };
        }

        // Not following, or a concurrent unfollow got there first: the same answer.
        if (!await followersRepository.UnfollowAsync(request.ReadingListId, currentUser.Id))
        {
            return OperationResult.AlreadyDone("NotFollowing", "أنت لا تتابع هذه القائمة");
        }

        await readingListsRepository.AdjustFollowersCountAsync(request.ReadingListId, -1);

        logger.LogInformation("User {UserId} successfully unfollowed reading list {ListId}", currentUser.Id, request.ReadingListId);

        return new OperationResult
        {
            Success = true,
            Message = $"ألغيت متابعة «{readingList.Name}»"
        };
    }
}
