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
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        logger.LogInformation("Deleting reading list {ListId} for user {UserId}", request.ReadingListId, currentUser.Id);

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("Reading list not found");

        if (readingList.UserId != currentUser.Id)
        {
            throw new ForbidException("You don't own this reading list");
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
            Message = "Failed to delete reading list"
        };
    }
}
