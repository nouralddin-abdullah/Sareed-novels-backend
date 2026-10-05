using Application.Chapters.DTOS;
using Application.Chapters.Scheduling;
using MediatR;

namespace Application.Chapters.Queries.GetChaptersAuthor;

public class GetChaptersAuthorQuery(Guid novelId) : IRequest<IEnumerable<ChaptersAuthorDTO>>, IReadsNovel
{
    public Guid NovelId { get; set; } = novelId;

    Guid? IReadsNovel.ReadNovelId => NovelId;
}
