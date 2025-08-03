using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Commands.DeleteReviewLike;

public class DeleteReviewLikeCommandHandler(ILogger<DeleteReviewLikeCommandHandler> logger, IUserContext userContext, IReviewLikesRepository reviewLikesRepository, IReviewsRepository reviewsRepository) : IRequestHandler<DeleteReviewLikeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(DeleteReviewLikeCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Liking a review {@review}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var review = await reviewsRepository.GetReviewById(request.ReviewId) ?? throw new NotFoundException("Review not found");
        var existingLike = await reviewLikesRepository.GetUserLikeForReview(currentUser.Id, request.ReviewId);
        if (existingLike == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You haven't like this review indeed"
            };
        }
        var result = await reviewLikesRepository.UnLikeReview(currentUser.Id, request.ReviewId);
        if (!result)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Failed to unlike review"
            };
        }

        return new OperationResult
        {
            Success = true,
            Message = "Review unliked successfully"
        };

    }
}
