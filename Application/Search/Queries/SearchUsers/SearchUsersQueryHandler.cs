using Application.Common;
using Application.Search.DTOs;
using Application.Services;
using MediatR;

namespace Application.Search.Queries.SearchUsers;

public class SearchUsersQueryHandler(IUserSearchService userSearchService) 
    : IRequestHandler<SearchUsersQuery, PagedResult<UserSearchResult>>
{
    public async Task<PagedResult<UserSearchResult>> Handle(SearchUsersQuery request, CancellationToken cancellationToken)
    {
        return await userSearchService.SearchUsersAsync(request.Request, cancellationToken);
    }
}
