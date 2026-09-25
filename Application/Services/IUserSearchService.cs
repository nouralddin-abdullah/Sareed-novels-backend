using Application.Common;
using Application.Search.DTOs;

namespace Application.Services;

/// <summary>User search, served straight from SQL (normalized User.SearchName); there is no index to maintain.</summary>
public interface IUserSearchService
{
    Task<PagedResult<UserSearchResult>> SearchUsersAsync(SearchUsersRequest request, CancellationToken cancellationToken = default);
}
