using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.ReadingLists.Commands.FollowReadingList;

public class FollowReadingListCommand(Guid readingListId) : IRequest<OperationResult>
{
    public Guid ReadingListId { get; set; } = readingListId;
}
