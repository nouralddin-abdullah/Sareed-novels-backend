using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Posts.Commands.LikePost;

public class LikePostCommandHandler(
    ILogger<LikePostCommandHandler> logger,
    IPostsRepository postsRepository,
    IPostLikesRepository postLikesRepository,
    IUserContext userContext) : IRequestHandler<LikePostCommand, OperationResult>
{
    public async Task<OperationResult> Handle(LikePostCommand request, CancellationToken cancellationToken)
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
        if (existingLike != null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Already liked this post"
            };
        }

        var postLike = new PostLike
        {
            UserId = currentUser.Id,
            PostId = request.PostId,
            CreatedAt = DateTime.UtcNow
        };

        await postLikesRepository.LikePost(postLike);
        
        post.IncrementLikeCount();
        await postsRepository.UpdatePost(post);
        
        logger.LogInformation("User {UserId} liked post {PostId}", currentUser.Id, request.PostId);

        return new OperationResult
        {
            Success = true,
            Message = "Post liked successfully"
        };
    }
}
