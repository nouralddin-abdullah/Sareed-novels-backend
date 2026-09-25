using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Commands.AddNovelToList;

public class AddNovelToListCommandHandler(
    ILogger<AddNovelToListCommandHandler> logger,
    IReadingListsRepository readingListsRepository,
    IReadingListNovelsRepository readingListNovelsRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext) : IRequestHandler<AddNovelToListCommand, OperationResult>
{
    public async Task<OperationResult> Handle(AddNovelToListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("Reading list not found");

        if (readingList.UserId != currentUser.Id)
        {
            throw new ForbidException("You don't own this reading list");
        }

        var novel = await novelsRepository.GetOne(request.NovelId)
            ?? throw new NotFoundException("Novel not found");

        // Check if novel is publicly visible
        if (!novel.IsPubliclyVisible)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Cannot add deleted or draft novels to reading list"
            };
        }

        // Check if novel is already in list (checks raw existence)
        if (await readingListNovelsRepository.IsNovelInListAsync(request.ReadingListId, request.NovelId))
        {
            return new OperationResult
            {
                Success = false,
                Message = "Novel is already in this reading list"
            };
        }

        var readingListNovel = new ReadingListNovel
        {
            ReadingListId = request.ReadingListId,
            NovelId = request.NovelId,
            AddedAt = DateTime.UtcNow,
            OrderIndex = await readingListNovelsRepository.GetNextOrderIndexAsync(request.ReadingListId)
        };

        var result = await readingListNovelsRepository.AddNovelAsync(readingListNovel);

        if (result)
        {
            await readingListsRepository.AdjustNovelsCountAsync(request.ReadingListId, +1);

            logger.LogInformation("Novel {NovelId} added to reading list {ListId}", request.NovelId, request.ReadingListId);

            return new OperationResult
            {
                Success = true,
                Message = "Novel added to reading list"
            };
        }

        return new OperationResult
        {
            Success = false,
            Message = "Failed to add novel to reading list"
        };
    }
}
