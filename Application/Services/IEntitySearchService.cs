using Application.Common;
using Application.Entities.DTOs;

namespace Application.Services;

public interface IEntitySearchService
{
    // Search operations
    Task<PagedResult<EntityListDTO>> SearchEntitiesAsync(
        Guid novelId,
        string? query = null,
        string? section = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
    
    // Index management
    Task<bool> IndexEntityAsync(Guid entityId);
    Task<bool> UpdateEntityInIndexAsync(Guid entityId);
    Task<bool> DeleteEntityFromIndexAsync(Guid entityId);
    
    // Bulk operations
    Task<bool> BulkIndexEntitiesAsync(IEnumerable<Guid> entityIds);
    Task<int> ReindexNovelEntitiesAsync(Guid novelId);
    
    // Index health
    Task<bool> EnsureIndexExistsAsync();
    
    // Analytics
    Task<Dictionary<string, int>> GetEntityTypeCountsAsync(Guid novelId);
    Task<List<string>> GetAttributeKeysAsync(Guid novelId, string entityType);
}
