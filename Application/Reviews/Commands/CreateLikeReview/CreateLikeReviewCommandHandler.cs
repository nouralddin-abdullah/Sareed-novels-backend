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
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var review = await reviewsRepository.GetReviewById(request.ReviewId) ?? throw new NotFoundException("المراجعة غير موجودة", "ReviewNotFound");
        if (review.ReviewerId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Code = "CannotLikeOwnContent",
                Message = "لا يمكنك الإعجاب بمراجعتك"
            };
        }
        // Inserts the like and bumps LikeCount in one transaction; a concurrent duplicate is a no-op here.
        if (!await reviewLikesRepository.LikeReview(currentUser.Id, request.ReviewId))
        {
            return OperationResult.AlreadyDone("AlreadyLiked", "سبق أن أعجبت بهذه المراجعة");
        }
        
        // Fire-and-forget: Send notification
        _ = SendLikeOnReviewNotificationInBackground(review.ReviewerId, currentUser.Id, request.ReviewId, review.NovelId);

        return new OperationResult
        {
            Success = true,
            Message = "حُفظ إعجابك بالمراجعة"
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
