using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Reviews.Commands.DeleteReviewLike;

public class DeleteReviewLikeCommand(Guid reviewId) : IRequest<OperationResult>
{
    public Guid ReviewId { get; set; } = reviewId;
}
