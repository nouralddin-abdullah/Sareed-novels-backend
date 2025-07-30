using Application.Common;
using Application.Users.DTOS;
using MediatR;

namespace Application.Users.Queries.GetFollowingList;

public class GetFollowingListQuery : IRequest<PagedResult<FollowedDto>>
{
    public string UserId { get; set; } = default!;
    public int PageSize { get; set; }
    public int PageNumber { get; set; }
}
