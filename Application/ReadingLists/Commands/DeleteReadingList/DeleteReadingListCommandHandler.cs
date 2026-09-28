using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Commands.DeleteReadingList;

public class DeleteReadingListCommandHandler(
    ILogger<DeleteReadingListCommandHandler> logger,
    IReadingListsRepository readingListsRepository,
    IUserContext userContext) : IRequestHandler<DeleteReadingListCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DeleteReadingListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("Deleting reading list {ListId} for user {UserId}", request.ReadingListId, currentUser.Id);

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("القائمة غير موجودة", "ReadingListNotFound");

        if (readingList.UserId != currentUser.Id)
        {
            throw new ForbidException("هذا الإجراء متاح لصاحب القائمة فقط", "NotOwner");
        }

        var result = await readingListsRepository.DeleteAsync(request.ReadingListId);

        if (result)
        {
            logger.LogInformation("Reading list {ListId} deleted successfully", request.ReadingListId);
            return new OperationResult
            {
                Success = true,
                Message = "Reading list deleted successfully"
            };
        }

        return new OperationResult
        {
            Success = false,
            Code = "OperationFailed",
            Message = "Failed to delete reading list"
        };
    }
}
