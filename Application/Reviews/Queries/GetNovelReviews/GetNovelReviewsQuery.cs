using Application.Common;
using Application.Reviews.DTO;
using MediatR;

namespace Application.Reviews.Queries.GetNovelReviews;

public class GetNovelReviewsQuery(Guid novelId, int pageSize, int pageNumber, string sorting) : IRequest<PagedResult<ReviewsDTO>>
{
    public Guid NovelId { get; set; } = novelId;
    public string Sorting { get; set; } = sorting;
    public int PageSize { get; set; } = pageSize;
    public int PageNumber { get; set; } = pageNumber;
}
