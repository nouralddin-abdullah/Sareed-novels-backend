using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Posts.Commands.UnlikePost;

public class UnlikePostCommandHandler(
    ILogger<UnlikePostCommandHandler> logger,
    IPostsRepository postsRepository,
    IPostLikesRepository postLikesRepository,
    IUserContext userContext) : IRequestHandler<UnlikePostCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UnlikePostCommand request, CancellationToken cancellationToken)
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

        var existingLike = await postLikesRepository.GetUserLikeForPost(currentUser.Id, request.PostId);
        if (existingLike == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Post not liked yet"
            };
        }

        await postLikesRepository.UnLikePost(currentUser.Id, request.PostId);
        
        post.DecrementLikeCount();
        await postsRepository.UpdatePost(post);
        
        logger.LogInformation("User {UserId} unliked post {PostId}", currentUser.Id, request.PostId);

        return new OperationResult
        {
            Success = true,
            Message = "Post unliked successfully"
        };
    }
}
