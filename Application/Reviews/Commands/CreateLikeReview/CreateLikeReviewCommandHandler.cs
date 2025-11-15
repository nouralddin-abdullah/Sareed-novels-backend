using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Commands.CreateLikeReview;

internal class CreateLikeReviewCommandHandler(
    ILogger<CreateLikeReviewCommandHandler> logger, 
    IUserContext userContext, 
    IReviewLikesRepository reviewLikesRepository, 
    IReviewsRepository reviewsRepository,
    IServiceProvider serviceProvider) : IRequestHandler<CreateLikeReviewCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateLikeReviewCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Liking a review {@review}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var review = await reviewsRepository.GetReviewById(request.ReviewId) ?? throw new NotFoundException("Review not found");
        if (review.ReviewerId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You cannot like your own review"
            };
        }
        var existingLike = await reviewLikesRepository.GetUserLikeForReview(currentUser.Id, request.ReviewId);
        if (existingLike != null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You already liked this review"
            };
        }

        var reviewLike = new ReviewLike
        {
            UserId = currentUser.Id,
            ReviewId = request.ReviewId
        };
        var result = await reviewLikesRepository.LikeReview(reviewLike);
        if (!result)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Failed to like this review"
            };
        }
        
        // Fire-and-forget: Send notification
        _ = SendLikeOnReviewNotificationInBackground(review.ReviewerId, currentUser.Id, request.ReviewId, review.NovelId);

        return new OperationResult
        {
            Success = true,
            Message = "Review Liked successfully"
        };
    }
    
    private async Task SendLikeOnReviewNotificationInBackground(string reviewAuthorId, string likerUserId, Guid reviewId, Guid novelId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
            var backgroundNotificationService = scope.ServiceProvider.GetRequiredService<Application.Services.INotificationService>();
            var backgroundNovelsRepository = scope.ServiceProvider.GetRequiredService<INovelsRepository>();
            
            var liker = await backgroundUserManager.FindByIdAsync(likerUserId);
            var novel = await backgroundNovelsRepository.GetOne(novelId);
            
            if (liker != null && novel != null)
            {
                await backgroundNotificationService.SendLikeOnReviewNotification(reviewAuthorId, liker, reviewId, novel);
                logger.LogDebug("Sent LikeOnReview notification to user {UserId}", reviewAuthorId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send LikeOnReview notification");
        }
    }
}
