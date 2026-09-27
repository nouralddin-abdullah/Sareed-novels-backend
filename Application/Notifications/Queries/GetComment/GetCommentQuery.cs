using Application.Notifications.DTOs;
using MediatR;

namespace Application.Notifications.Queries.GetComment;

public class GetCommentQuery(Guid commentId, int pageSize = 10) : IRequest<CommentDetailDto>
{
    public Guid CommentId { get; set; } = commentId;

    /// <summary>The page size the client reads the comment's list with; the context's PageNumber is for it.</summary>
    public int PageSize { get; set; } = pageSize;
}
