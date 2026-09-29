using Application.Reviews.Queries;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Commands.UpdateReview;

/// <summary>
/// Its author edits a review (#34), which keeps its id, likes and creation date (deleting it and writing it again lost
/// the likes). A review that isn't under this novel is not found (404 ReviewNotFound), another reader's is refused (403
/// NotOwner). Saving without a change writes nothing and leaves <c>updatedAt</c> as it was.
/// </summary>
public class UpdateReviewCommandHandler(
    ILogger<UpdateReviewCommandHandler> logger,
    IUserContext userContext,
    IMapper mapper,
    IReviewsRepository reviewsRepository,
    IReviewLikesRepository reviewLikesRepository,
    TimeProvider time) : IRequestHandler<UpdateReviewCommand, UpdateReviewResult>
{
    public const string NotFoundMessage = "المراجعة غير موجودة";
    public const string NotOwnerMessage = "يمكنك تعديل مراجعاتك فقط";
    public const string UpdatedMessage = "تم تعديل مراجعتك";

    public async Task<UpdateReviewResult> Handle(UpdateReviewCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var review = await reviewsRepository.GetReviewById(request.ReviewId);
        if (review is null || review.NovelId != request.NovelId)
        {
            throw new NotFoundException(NotFoundMessage, "ReviewNotFound");
        }
        if (review.ReviewerId != currentUser.Id)
        {
            throw new ForbidException(NotOwnerMessage, "NotOwner");
        }

        if (request.Edit.Changes(review))
        {
            // Also recomputes the review's own average (what rankings read) and the novel's review stats (what the novel
            // page, lists and search show), as creating and deleting a review do.
            if (!await reviewsRepository.UpdateReview(review, request.Edit, time.GetUtcNow().UtcDateTime))
            {
                // Deleted since it was read.
                throw new NotFoundException(NotFoundMessage, "ReviewNotFound");
            }
            logger.LogInformation("User {UserId} edited review {ReviewId} of novel {NovelId}", currentUser.Id, review.Id, review.NovelId);
        }

        // Read back as the novel's review list reads reviews, so the app can replace it in that list.
        var listed = await reviewsRepository.GetReviewAsListedAsync(review.Id)
            ?? throw new NotFoundException(NotFoundMessage, "ReviewNotFound");
        var reviewDtos = await ReviewListDtos.Build([listed], mapper, reviewLikesRepository, currentUser);

        return new UpdateReviewResult
        {
            Success = true,
            Message = UpdatedMessage,
            Review = reviewDtos.Single()
        };
    }
}
