using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.ReadingLists.Commands.UnfollowReadingList;

public class UnfollowReadingListCommand(Guid readingListId) : IRequest<OperationResult>
{
    public Guid ReadingListId { get; set; } = readingListId;
}
