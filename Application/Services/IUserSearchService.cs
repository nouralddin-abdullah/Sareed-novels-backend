using Application.Common;
using Application.Search.DTOs;

namespace Application.Services;

public interface IUserSearchService
{
    // Search operations
    Task<PagedResult<UserSearchResult>> SearchUsersAsync(SearchUsersRequest request, CancellationToken cancellationToken = default);
    
    // Index management (data integrity)
    Task<bool> IndexUserAsync(string userId);
    Task<bool> UpdateUserInIndexAsync(string userId);
    Task<bool> DeleteUserFromIndexAsync(string userId);
    
    // Admin operations
    Task<bool> EnsureIndexExistsAsync();
    Task<int> ReindexAllUsersAsync();
}
