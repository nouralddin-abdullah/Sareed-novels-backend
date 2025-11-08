using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Commands.DeleteReview;

internal class DeleteReviewHandler(
    ILogger<DeleteReviewHandler> logger, 
    IUserContext userContext, 
    INovelsRepository novelsRepository, 
    IReviewsRepository reviewsRepository,
    ISearchIndexQueueService searchIndexQueue,
    IServiceProvider serviceProvider) : IRequestHandler<DeleteReviewCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DeleteReviewCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting review on novel {@novel}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var existingReview = await reviewsRepository.GetUserReviewForNovel(currentUser.Id, request.NovelId) ?? throw new NotFoundException("No review found for this user");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");

        novel.RemoveReviewFromAverages(existingReview);

        var result = await reviewsRepository.DeleteReviewWithNovelUpdate(existingReview, novel);

        if (!result)
        {
            logger.LogError("Failed to delete review for novel {NovelId}, user {UserId}",
                request.NovelId, currentUser.Id);

            return new OperationResult
            {
                Success = false,
                Message = "Failed to delete review"
            };
        }

        // Fire-and-forget: Decrement user's reviews count
        _ = DecrementUserReviewsCountInBackground(currentUser.Id);

        logger.LogInformation("Successfully deleted review for novel {NovelId}, user {UserId}",
            request.NovelId, currentUser.Id);

        // Queue for Elasticsearch update (review stats changed)
        await searchIndexQueue.QueueUpdateAsync(request.NovelId);
        logger.LogDebug("Queued novel {NovelId} for search index update (review deleted)", request.NovelId);

        return new OperationResult
        {
            Success = true,
            Message = "Review deleted successfully"
        };
    }
    
    private async Task DecrementUserReviewsCountInBackground(string userId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            var user = await backgroundUserManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.DecrementReviewsCount();
                await backgroundUserManager.UpdateAsync(user);
            }
            logger.LogDebug("Successfully decremented reviews count for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to decrement reviews count for user {UserId}", userId);
        }
    }
}

