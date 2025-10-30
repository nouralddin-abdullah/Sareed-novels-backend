using Application.Chapters.DTOS;
using MediatR;

namespace Application.Chapters.Queries.GetChapterReader;

public class GetChapterReaderQuery(Guid novelId, Guid chapterId) : IRequest<ChapterSingleReaderDTO>
{
    public Guid NovelId { get; set; } = novelId;
    public Guid ChapterId { get; set; } = chapterId;
}
