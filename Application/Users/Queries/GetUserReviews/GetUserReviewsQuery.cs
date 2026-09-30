using Application.Common;
using Application.Users.DTOS;
using MediatR;

namespace Application.Users.Queries.GetUserReviews;

/// <summary>GET /api/User/{userName}/reviews: a page of the member's reviews, newest first (#54).</summary>
public class GetUserReviewsQuery(string userName, int pageNumber, int pageSize) : IRequest<PagedResult<ProfileReviewDTO>>
{
    public string UserName { get; } = userName;
    public int PageNumber { get; } = pageNumber;
    public int PageSize { get; } = pageSize;
}
