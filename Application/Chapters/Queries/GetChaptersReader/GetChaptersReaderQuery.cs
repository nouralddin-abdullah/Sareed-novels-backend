using Application.Chapters.DTOS;
using Application.Chapters.Scheduling;
using MediatR;

namespace Application.Chapters.Queries.GetChaptersReader;

public class GetChaptersReaderQuery(Guid novelId) : IRequest<IEnumerable<ChaptersDTO>>, IReadsNovel
{
    public Guid NovelId { get; set; } = novelId;

    Guid? IReadsNovel.ReadNovelId => NovelId;
}
