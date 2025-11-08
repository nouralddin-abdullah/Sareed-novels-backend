using Application.Common;
using Application.Comments.DTOS;
using MediatR;

namespace Application.Comments.Queries.GetPostComments;

public class GetPostCommentsQuery(Guid postId, int pageNumber, int pageSize, string sorting) : IRequest<PagedResult<CommentsDTO>>
{
    public Guid PostId { get; set; } = postId;
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
    public string Sorting { get; set; } = sorting;
}
