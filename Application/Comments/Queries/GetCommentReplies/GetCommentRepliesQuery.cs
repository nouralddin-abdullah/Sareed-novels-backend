using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Application.Comments.DTOS;
using Application.Common;
using MediatR;

namespace Application.Comments.Queries.GetCommentReplies;

public class GetCommentRepliesQuery(Guid parentCommentId, int pageNumber = 1, int pageSize = 20, string sorting = "oldest") : IRequest<PagedResult<CommentReplyDTO>>
{
    public Guid ParentCommentId { get; set; } = parentCommentId;
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
    public string Sorting { get; set; } = sorting;
}
