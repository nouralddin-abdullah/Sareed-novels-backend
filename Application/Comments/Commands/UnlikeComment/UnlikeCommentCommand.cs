using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Comments.Commands.UnlikeComment;

public class UnlikeCommentCommand : IRequest<OperationResult>
{
    public Guid CommentId { get; set; }
}