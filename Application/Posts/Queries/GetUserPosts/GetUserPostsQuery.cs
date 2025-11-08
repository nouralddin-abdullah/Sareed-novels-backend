using Application.Common;
using Application.Posts.DTOs;
using MediatR;

namespace Application.Posts.Queries.GetUserPosts;

public class GetUserPostsQuery(string userId, int pageNumber, int pageSize) : IRequest<PagedResult<PostDTO>>
{
    public string UserId { get; set; } = userId;
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
}
