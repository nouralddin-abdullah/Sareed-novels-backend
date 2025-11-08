using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Posts.Commands.UnlikePost;

public class UnlikePostCommand(Guid postId) : IRequest<OperationResult>
{
    public Guid PostId { get; set; } = postId;
}
