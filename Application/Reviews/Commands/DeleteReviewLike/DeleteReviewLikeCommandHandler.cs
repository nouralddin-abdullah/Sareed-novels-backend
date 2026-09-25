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
        logger.LogInformation("Unliking a review {@review}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var review = await reviewsRepository.GetReviewById(request.ReviewId) ?? throw new NotFoundException("Review not found");
        if (!await reviewLikesRepository.UnLikeReview(currentUser.Id, request.ReviewId))
        {
            return new OperationResult
            {
                Success = false,
                Message = "You haven't liked this review"
            };
        }

        return new OperationResult
        {
            Success = true,
            Message = "Review unliked successfully"
        };

    }
}
