using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.ReadingLists.Commands.AddNovelToList;

public class AddNovelToListCommand : IRequest<OperationResult>
{
    public Guid ReadingListId { get; set; }
    public Guid NovelId { get; set; }
}
