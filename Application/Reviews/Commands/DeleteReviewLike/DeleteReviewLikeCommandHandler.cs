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
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var review = await reviewsRepository.GetReviewById(request.ReviewId) ?? throw new NotFoundException("المراجعة غير موجودة", "ReviewNotFound");
        if (!await reviewLikesRepository.UnLikeReview(currentUser.Id, request.ReviewId))
        {
            return OperationResult.AlreadyDone("NotLiked", "لم تُبدِ إعجابك بهذه المراجعة");
        }

        return new OperationResult
        {
            Success = true,
            Message = "أُلغي إعجابك بالمراجعة"
        };

    }
}
