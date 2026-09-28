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
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("القائمة غير موجودة", "ReadingListNotFound");

        if (readingList.UserId != currentUser.Id)
        {
            throw new ForbidException("هذا الإجراء متاح لصاحب القائمة فقط", "NotOwner");
        }

        // Not in the list, or a concurrent removal got there first: the same answer.
        if (!await readingListNovelsRepository.RemoveNovelAsync(request.ReadingListId, request.NovelId))
        {
            return OperationResult.AlreadyDone("NotInList", "هذه الرواية ليست في القائمة");
        }

        await readingListsRepository.AdjustNovelsCountAsync(request.ReadingListId, -1);

        logger.LogInformation("Novel {NovelId} removed from reading list {ListId}", request.NovelId, request.ReadingListId);

        return new OperationResult
        {
            Success = true,
            Message = "أُزيلت الرواية من القائمة"
        };
    }
}
