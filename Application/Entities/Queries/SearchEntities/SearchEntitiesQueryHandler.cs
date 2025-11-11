using Application.Common;
using Application.Entities.DTOs;
using Application.Services;
using MediatR;

namespace Application.Entities.Queries.SearchEntities;

public class SearchEntitiesQueryHandler(IEntitySearchService entitySearchService) 
    : IRequestHandler<SearchEntitiesQuery, PagedResult<EntityListDTO>>
{
    public async Task<PagedResult<EntityListDTO>> Handle(SearchEntitiesQuery request, CancellationToken cancellationToken)
    {
        var searchResults = await entitySearchService.SearchEntitiesAsync(
            request.NovelId,
            request.Query,
            request.Section,
            request.PageNumber,
            request.PageSize);

        return searchResults;
    }
}
