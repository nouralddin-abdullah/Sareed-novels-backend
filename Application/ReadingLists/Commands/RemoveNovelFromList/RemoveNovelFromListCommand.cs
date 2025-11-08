using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.ReadingLists.Commands.RemoveNovelFromList;

public class RemoveNovelFromListCommand(Guid readingListId, Guid novelId) : IRequest<OperationResult>
{
    public Guid ReadingListId { get; set; } = readingListId;
    public Guid NovelId { get; set; } = novelId;
}