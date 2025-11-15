using Application.Notifications.DTOs;
using MediatR;

namespace Application.Notifications.Queries.GetComment;

public class GetCommentQuery(Guid commentId) : IRequest<CommentDetailDto>
{
    public Guid CommentId { get; set; } = commentId;
}
