using Application.Common;
using Application.Search.DTOs;

namespace Application.Services;

public interface INovelSearchService
{
    // Search operations
    Task<PagedResult<NovelSearchResult>> SearchNovelsAsync(
        SearchNovelsRequest request, 
        CancellationToken cancellationToken = default);
    
    // Index management (data integrity)
    Task<bool> IndexNovelAsync(Guid novelId);
    Task<bool> UpdateNovelInIndexAsync(Guid novelId);
    Task<bool> DeleteNovelFromIndexAsync(Guid novelId);
    
    // Bulk operations
    Task<bool> BulkIndexNovelsAsync(IEnumerable<Guid> novelIds);
    Task<int> ReindexAllNovelsAsync(); // For migrations
    
    // Index health
    Task<bool> EnsureIndexExistsAsync();
}
