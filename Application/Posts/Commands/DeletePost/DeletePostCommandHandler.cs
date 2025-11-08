using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Posts.Commands.DeletePost;

public class DeletePostCommandHandler(
    ILogger<DeletePostCommandHandler> logger,
    IPostsRepository postsRepository,
    IUserContext userContext) : IRequestHandler<DeletePostCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DeletePostCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        var post = await postsRepository.GetPostById(request.PostId);
        if (post == null || post.IsDeleted)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Post not found"
            };
        }

        if (post.UserId != currentUser.Id)
        {
            throw new ForbidException("You can only delete your own posts");
        }
        
        await postsRepository.DeletePost(request.PostId);
        
        logger.LogInformation("Post {PostId} deleted by user {UserId}", request.PostId, currentUser.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Post deleted successfully"
        };
    }
}
