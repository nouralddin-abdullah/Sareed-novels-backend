using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Commands.CreateReview;

public class CreateReviewCommandHandler(
    ILogger<CreateReviewCommandHandler> logger, 
    INovelsRepository novelsRepository, 
    IUserContext userContext, 
    IMapper mapper, 
    IReviewsRepository reviewsRepository,
    ISearchIndexQueueService searchIndexQueue,
    IServiceProvider serviceProvider) : IRequestHandler<CreateReviewCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateReviewCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating new review {@review}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        if (novel.AuthorId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You cannot review your own novel"
            };
        }
        var existingReview = await reviewsRepository.GetUserReviewForNovel(currentUser.Id, request.NovelId);
        if (existingReview != null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You have already reviewed this novel"
            };
        }
        var review = mapper.Map<Review>(request);
        review.CreatedAt = DateTime.UtcNow;
        review.NovelId = novel.Id;
        review.ReviewerId = currentUser.Id;
        var result = await reviewsRepository.CreateOne(review);
        if (!result)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Failed to create review"
            };
        }
        novel.AddReviewToAverages(review);
        await novelsRepository.UpdateOne(novel);
        
        // Fire-and-forget: Send notification to novel author
        _ = SendReviewNotificationInBackground(novel.AuthorId, currentUser.Id, review.Id, novel.Id);
        
        // Fire-and-forget: Increment user's reviews count
        _ = IncrementUserReviewsCountInBackground(currentUser.Id);

        // Queue for Elasticsearch update (review stats changed)
        await searchIndexQueue.QueueUpdateAsync(novel.Id);
        logger.LogDebug("Queued novel {NovelId} for search index update (review added)", novel.Id);
        
        return new OperationResult
        {
            Success = true,
            Message = "Review was created"
        };

    }
    
    private async Task SendReviewNotificationInBackground(string novelAuthorId, string reviewerUserId, Guid reviewId, Guid novelId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var backgroundNotificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var backgroundNovelsRepository = scope.ServiceProvider.GetRequiredService<INovelsRepository>();
            
            var reviewerUser = await backgroundUserManager.FindByIdAsync(reviewerUserId);
            var novel = await backgroundNovelsRepository.GetOne(novelId);
            
            if (reviewerUser != null && novel != null)
            {
                await backgroundNotificationService.SendReviewOnNovelNotification(novelAuthorId, reviewerUser, reviewId, novel);
                logger.LogDebug("Sent ReviewOnNovel notification to user {UserId}", novelAuthorId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send ReviewOnNovel notification in background");
        }
    }
    
    private async Task IncrementUserReviewsCountInBackground(string userId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            var user = await backgroundUserManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.IncrementReviewsCount();
                await backgroundUserManager.UpdateAsync(user);
            }
            logger.LogDebug("Successfully incremented reviews count for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to increment reviews count for user {UserId}", userId);
        }
    }
}
