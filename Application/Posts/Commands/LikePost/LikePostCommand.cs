using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Posts.Commands.LikePost;

public class LikePostCommand(Guid postId) : IRequest<OperationResult>
{
    public Guid PostId { get; set; } = postId;
}
