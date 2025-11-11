using Application.Entities.DTOs;
using Application.Common;
using MediatR;

namespace Application.Entities.Queries.SearchEntities;

public class SearchEntitiesQuery : IRequest<PagedResult<EntityListDTO>>
{
    public Guid NovelId { get; set; }
    public string? Query { get; set; }
    public string? Section { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
