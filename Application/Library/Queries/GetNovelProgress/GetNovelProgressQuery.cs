using Application.Library.DTOs;
using MediatR;

namespace Application.Library.Queries.GetNovelProgress;

public class GetNovelProgressQuery(Guid novelId) : IRequest<NovelProgressDTO?>
{
    public Guid NovelId { get; set; } = novelId;
}
