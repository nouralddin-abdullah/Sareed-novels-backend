using Application.Common;
using Application.Entities.DTOs;
using MediatR;

namespace Application.Entities.Queries.GetNovelEntities;

public class GetNovelEntitiesQuery : IRequest<PagedResult<EntityListDTO>>
{
    public Guid NovelId { get; set; }
    public string? Section { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
