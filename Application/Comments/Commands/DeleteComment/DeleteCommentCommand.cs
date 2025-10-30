using MediatR;

namespace Application.Comments.Commands.DeleteComment;

public class DeleteCommentCommand(Guid commentId) : IRequest<bool>
{
    public Guid CommentId { get; set; } = commentId;
}
