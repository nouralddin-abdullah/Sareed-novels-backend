using Application.Chapters.DTOS;
using MediatR;

namespace Application.Chapters.Queries.GetChaptersReader;

public class GetChaptersReaderQuery(Guid novelId) : IRequest<IEnumerable<ChaptersDTO>>
{
    public Guid NovelId { get; set; } = novelId;
}
