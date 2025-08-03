using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Commands.CreateLikeReview;

internal class CreateLikeReviewCommandHandler(ILogger<CreateLikeReviewCommandHandler> logger, IUserContext userContext, IReviewLikesRepository reviewLikesRepository, IReviewsRepository reviewsRepository) : IRequestHandler<CreateLikeReviewCommand, OperationResult>
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

        return new OperationResult
        {
            Success = true,
            Message = "Review Liked successfully"
        };
    }
}
