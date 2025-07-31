using Application.Common;
using Application.Users.DTOS;
using MediatR;

namespace Application.Users.Queries.GetFollowersList;

public class GetFollowersListQuery(string userId, int? pageSize, int? pageNumber) : IRequest<PagedResult<FollowerDto>>
{
    public string UserId { get; set; } = userId;
    public int PageSize { get; set; } = pageSize ?? 10;
    public int PageNumber { get; set; } = pageNumber ?? 1;
}
