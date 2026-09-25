using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Commands.DeleteReview;

internal class DeleteReviewHandler(
    ILogger<DeleteReviewHandler> logger, 
    IUserContext userContext, 
    IReviewsRepository reviewsRepository) : IRequestHandler<DeleteReviewCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DeleteReviewCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting review on novel {@novel}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var existingReview = await reviewsRepository.GetUserReviewForNovel(currentUser.Id, request.NovelId) ?? throw new NotFoundException("No review found for this user");

        // Deletes the review's likes with it, uncounts it for the user and recomputes the novel's review stats.
        var result = await reviewsRepository.DeleteReview(existingReview);

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

        logger.LogInformation("Successfully deleted review for novel {NovelId}, user {UserId}",
            request.NovelId, currentUser.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Review deleted successfully"
        };
    }
}

