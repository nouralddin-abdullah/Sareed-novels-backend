using Application.Common;
using Application.Search.DTOs;
using MediatR;

namespace Application.Search.Queries.SearchUsers;

public record SearchUsersQuery(SearchUsersRequest Request) : IRequest<PagedResult<UserSearchResult>>;
