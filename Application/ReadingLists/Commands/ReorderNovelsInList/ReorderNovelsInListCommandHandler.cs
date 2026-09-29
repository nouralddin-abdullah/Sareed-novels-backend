using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.ReadingLists;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Commands.ReorderNovelsInList;

public class ReorderNovelsInListCommandHandler(
    ILogger<ReorderNovelsInListCommandHandler> logger,
    IReadingListsRepository readingListsRepository,
    IReadingListNovelsRepository readingListNovelsRepository,
    IUserContext userContext) : IRequestHandler<ReorderNovelsInListCommand, OperationResult>
{
    /// <summary>The ids weren't exactly the list's novels: one missing (added since), extra (removed since) or repeated.</summary>
    public const string MismatchCode = "NovelOrderMismatch";
    public const string MismatchMessage =
        "الترتيب المرسل لا يطابق روايات القائمة: يجب أن يضم كل رواية فيها مرة واحدة، ولا شيء غيرها. حدّث القائمة وحاول مرة أخرى.";

    public async Task<OperationResult> Handle(ReorderNovelsInListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException(ReadingListBlocks.NotFoundMessage, ReadingListBlocks.NotFoundCode);

        if (readingList.UserId != currentUser.Id)
        {
            throw new ForbidException("هذا الإجراء متاح لصاحب القائمة فقط", "NotOwner");
        }

        var result = await readingListNovelsRepository.ReorderAsync(request.ReadingListId, request.OrderedNovelIds, cancellationToken);
        switch (result)
        {
            case ReadingListReorderResult.ListNotFound:
                // Deleted since it was read above.
                throw new NotFoundException(ReadingListBlocks.NotFoundMessage, ReadingListBlocks.NotFoundCode);
            case ReadingListReorderResult.Mismatch:
                return new OperationResult { Success = false, Code = MismatchCode, Message = MismatchMessage };
        }

        logger.LogInformation("Reading list {ListId}: novels reordered ({Result})", request.ReadingListId, result);
        return new OperationResult
        {
            Success = true,
            Message = "حُفظ ترتيب الروايات"
        };
    }
}
