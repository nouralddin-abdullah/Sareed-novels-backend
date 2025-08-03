using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Reviews.Commands.DeleteReview;

public class DeleteReviewCommand(Guid novelId) : IRequest<OperationResult>
{
    public Guid NovelId { get; set; } = novelId;
}
