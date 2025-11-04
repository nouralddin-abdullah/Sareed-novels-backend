using Application.Comments.DTOS;
using Application.Common;
using MediatR;

namespace Application.Comments.Queries.GetChapterComments;

public class GetChapterCommentsQuery(Guid chapterId, int pageNumber = 1, int pageSize = 10, string sorting = "recent") : IRequest<PagedResult<CommentsDTO>>
{
    public Guid ChapterId { get; set; } = chapterId;
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
    public string Sorting { get; set; } = sorting;
}