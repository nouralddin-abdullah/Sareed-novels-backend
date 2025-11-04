using Application.Comments.DTOS;
using Application.Common;
using MediatR;

namespace Application.Comments.Queries.GetParagraphComments;

public class GetParagraphCommentsQuery(Guid paragraphId, int pageNumber, int pageSize, string sorting) : IRequest<PagedResult<CommentsDTO>>
{
    public Guid ParagraphId { get; set; } = paragraphId;
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
    public string Sorting { get; set; } = sorting;
}
