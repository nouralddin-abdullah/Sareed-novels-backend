using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Posts.Commands.LikePost;

public class LikePostCommandHandler(
    ILogger<LikePostCommandHandler> logger,
    IPostsRepository postsRepository,
    IPostLikesRepository postLikesRepository,
    IUserContext userContext,
    IServiceProvider serviceProvider) : IRequestHandler<LikePostCommand, OperationResult>
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

        // Inserts the like and bumps LikesCount in one transaction; a concurrent duplicate is a no-op here.
        if (!await postLikesRepository.LikePost(currentUser.Id, request.PostId))
        {
            return new OperationResult
            {
                Success = false,
                Message = "Already liked this post"
            };
        }

        // Fire-and-forget: Send notification
        _ = SendLikeOnPostNotificationInBackground(post.UserId, currentUser.Id, request.PostId);
        
        logger.LogInformation("User {UserId} liked post {PostId}", currentUser.Id, request.PostId);

        return new OperationResult
        {
            Success = true,
            Message = "Post liked successfully"
        };
    }
    
    private async Task SendLikeOnPostNotificationInBackground(string postAuthorId, string likerUserId, Guid postId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
            var backgroundNotificationService = scope.ServiceProvider.GetRequiredService<Application.Services.INotificationService>();
            
            var liker = await backgroundUserManager.FindByIdAsync(likerUserId);
            var postAuthor = await backgroundUserManager.FindByIdAsync(postAuthorId);
            
            if (liker != null && postAuthor != null)
            {
                await backgroundNotificationService.SendLikeOnPostNotification(postAuthorId, liker, postId, postAuthor.UserName!);
                logger.LogDebug("Sent LikeOnPost notification to user {UserId}", postAuthorId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send LikeOnPost notification");
        }
    }
}
