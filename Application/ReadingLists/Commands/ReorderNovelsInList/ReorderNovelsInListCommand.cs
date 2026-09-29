using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.ReadingLists.Commands.ReorderNovelsInList;

/// <summary>The owner puts the list's novels in a new order, given as every novel readers see on it.</summary>
public class ReorderNovelsInListCommand(Guid readingListId, IReadOnlyList<Guid> orderedNovelIds) : IRequest<OperationResult>
{
    public Guid ReadingListId { get; } = readingListId;
    public IReadOnlyList<Guid> OrderedNovelIds { get; } = orderedNovelIds;
}
