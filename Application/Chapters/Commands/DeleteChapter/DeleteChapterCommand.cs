using MediatR;

namespace Application.Chapters.Commands.DeleteChapter;

public class DeleteChapterCommand(Guid novelId, Guid chapterId) : IRequest<bool>
{
    public Guid NovelId { get; set; } = novelId;
    public Guid ChapterId { get; set; } = chapterId;
}
