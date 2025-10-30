using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Chapters.Commands.ReorderChapter;

public class ReorderChaptersCommand(Guid novelId, List<Guid> orderedChapterIds) : IRequest<bool>
{
    public Guid NovelId { get; set; } = novelId;
    public List<Guid> OrderedChapterIds { get; set; } = orderedChapterIds;
}
