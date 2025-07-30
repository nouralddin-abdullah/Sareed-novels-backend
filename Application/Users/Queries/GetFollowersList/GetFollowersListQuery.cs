using Application.Common;
using Application.Users.DTOS;
using MediatR;

namespace Application.Users.Queries.GetFollowersList;

public class GetFollowersListQuery : IRequest<PagedResult<FollowerDto>>
{
    public string UserId { get; set; } = default!;
    public int PageSize { get; set; }
    public int PageNumber { get; set; }
}
