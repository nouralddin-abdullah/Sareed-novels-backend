using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Reviews.Commands.CreateLikeReview;

public class CreateLikeReviewCommand(Guid reviewId) : IRequest<OperationResult>
{
    public Guid ReviewId { get; set; } = reviewId;
}
