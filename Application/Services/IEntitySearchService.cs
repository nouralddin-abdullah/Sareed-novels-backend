using Application.Common;
using Application.Entities.DTOs;

namespace Application.Services;

/// <summary>Novel wiki search, served straight from SQL (normalized NovelEntity.SearchText); no index to maintain.</summary>
public interface IEntitySearchService
{
    Task<PagedResult<EntityListDTO>> SearchEntitiesAsync(
        Guid novelId,
        string? query = null,
        string? section = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
}
