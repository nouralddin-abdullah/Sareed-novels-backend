using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.ReadingLists.Commands.DeleteReadingList;

public class DeleteReadingListCommand(Guid readingListId) : IRequest<OperationResult>
{
    public Guid ReadingListId { get; set; } = readingListId;
}
