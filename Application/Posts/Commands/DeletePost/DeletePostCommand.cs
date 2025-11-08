using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Posts.Commands.DeletePost;

public class DeletePostCommand(Guid postId) : IRequest<OperationResult>
{
    public Guid PostId { get; set; } = postId;
}
