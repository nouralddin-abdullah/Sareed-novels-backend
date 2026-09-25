using Application.Common;
using Application.Search.DTOs;
using Application.Services;
using Application.Users;
using Domain.Repositories;
using MediatR;

namespace Application.Search.Queries.SearchUsers;

public class SearchUsersQueryHandler(
    IUserSearchService userSearchService,
    IUserContext userContext,
    IUsersRepository usersRepository)
    : IRequestHandler<SearchUsersQuery, PagedResult<UserSearchResult>>
{
    public async Task<PagedResult<UserSearchResult>> Handle(SearchUsersQuery request, CancellationToken cancellationToken)
    {
        var result = await userSearchService.SearchUsersAsync(request.Request, cancellationToken);

        // The web app shows a follow button on every hit; tell a signed-in caller whom they already follow.
        var currentUser = userContext.GetCurrentUser();
        var items = result.Items.ToList();
        if (currentUser != null && items.Count > 0)
        {
            var following = await usersRepository.IsFollowingBulkAsync(currentUser.Id, items.Select(u => u.Id));
            foreach (var user in items)
            {
                user.IsFollowing = following.GetValueOrDefault(user.Id, false);
            }
        }

        result.Items = items;
        return result;
    }
}
