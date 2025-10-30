using Application.Chapters.DTOS;
using MediatR;

namespace Application.Chapters.Queries.GetChaptersAuthor;

public class GetChaptersAuthorQuery(Guid novelId) : IRequest<IEnumerable<ChaptersDTO>>
{
    public Guid NovelId { get; set; } = novelId;
}
