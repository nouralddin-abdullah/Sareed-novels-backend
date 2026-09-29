using FluentValidation;

namespace Application.Reviews.Commands.UpdateReview;

/// <summary>The rules of writing a review (<see cref="ReviewRules"/>), for the fields an edit sends.</summary>
public class UpdateReviewRequestValidator : AbstractValidator<UpdateReviewRequest>
{
    public UpdateReviewRequestValidator()
    {
        // A score left out is null, which passes: it stays as it is.
        RuleFor(d => d.WritingQualityScore).ReviewScore(ReviewRules.WritingQuality);
        RuleFor(d => d.UpdatingStabilityScore).ReviewScore(ReviewRules.UpdatingStability);
        RuleFor(d => d.CharacterDevelopmentScore).ReviewScore(ReviewRules.CharacterDevelopment);
        RuleFor(d => d.WorldBuildingScore).ReviewScore(ReviewRules.WorldBuilding);

        // Left out keeps the text; empty removes it, as a review may be written without text.
        RuleFor(d => d.Content)
            .ReviewContent()
            .When(d => !string.IsNullOrWhiteSpace(d.Content));
    }
}
