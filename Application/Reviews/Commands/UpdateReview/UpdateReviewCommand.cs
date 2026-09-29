using Application.Reviews.DTO;
using Domain.Reviews;
using MediatR;

namespace Application.Reviews.Commands.UpdateReview;

/// <summary>
/// The signed-in reader edits her review <see cref="ReviewId"/> of novel <see cref="NovelId"/> (#34); the answer is
/// the review as the novel's review list returns it.
/// </summary>
public class UpdateReviewCommand(Guid novelId, Guid reviewId, UpdateReviewRequest request) : IRequest<ReviewsDTO>
{
    public Guid NovelId { get; } = novelId;
    public Guid ReviewId { get; } = reviewId;

    public ReviewEdit Edit { get; } = ReviewEdit.Of(
        request.WritingQualityScore,
        request.UpdatingStabilityScore,
        request.CharacterDevelopmentScore,
        request.WorldBuildingScore,
        request.Content,
        request.IsSpoiler);
}
