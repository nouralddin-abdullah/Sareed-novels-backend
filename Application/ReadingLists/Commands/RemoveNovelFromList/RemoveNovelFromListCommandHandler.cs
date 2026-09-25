using Application.ReadingLists.Commands.AddNovelToList;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Commands.RemoveNovelFromList;

public class RemoveNovelFromListCommandHandler(
    ILogger<RemoveNovelFromListCommandHandler> logger,
    IReadingListsRepository readingListsRepository,
    IReadingListNovelsRepository readingListNovelsRepository,
    IUserContext userContext) : IRequestHandler<RemoveNovelFromListCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RemoveNovelFromListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("Reading list not found");

        if (readingList.UserId != currentUser.Id)
        {
            throw new ForbidException("You don't own this reading list");
        }

        if (!await readingListNovelsRepository.IsNovelInListAsync(request.ReadingListId, request.NovelId))
        {
            return new OperationResult
            {
                Success = false,
                Message = "Novel is not in this reading list"
            };
        }
        var result = await readingListNovelsRepository.RemoveNovelAsync(request.ReadingListId, request.NovelId);
        if (result)
        {
            await readingListsRepository.AdjustNovelsCountAsync(request.ReadingListId, -1);

            logger.LogInformation("Novel {NovelId} removed from reading list {ListId}", request.NovelId, request.ReadingListId);

            return new OperationResult
            {
                Success = true,
                Message = "Novel removed from reading list"
            };
        }

        return new OperationResult
        {
            Success = false,
            Message = "Failed to remove novel from reading list"
        };
    }
}
