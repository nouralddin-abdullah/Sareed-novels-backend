using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Comments.Commands.LikeComment;

public class LikeCommentCommand : IRequest<OperationResult>
{
    public Guid CommentId { get; set; }
}