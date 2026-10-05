using Application.Chapters.DTOS;
using MediatR;

namespace Application.Chapters.Queries.GetChaptersAuthor;

public class GetChaptersAuthorQuery(Guid novelId) : IRequest<IEnumerable<ChaptersAuthorDTO>>
{
    public Guid NovelId { get; set; } = novelId;
}
