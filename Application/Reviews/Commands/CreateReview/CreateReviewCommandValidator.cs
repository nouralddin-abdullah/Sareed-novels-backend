using FluentValidation;

namespace Application.Reviews.Commands.CreateReview;

/// <summary>The rules of <see cref="ReviewRules"/>, which editing a review applies to the fields it sends.</summary>
public class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommandrRequest>
{
    public CreateReviewCommandValidator()
    {
        RuleFor(d => d.WritingQualityScore)
            .NotNull()
            .WithMessage(ReviewRules.MissingScoreMessage(ReviewRules.WritingQuality))
            .ReviewScore(ReviewRules.WritingQuality);

        RuleFor(d => d.UpdatingStabilityScore)
            .NotNull()
            .WithMessage(ReviewRules.MissingScoreMessage(ReviewRules.UpdatingStability))
            .ReviewScore(ReviewRules.UpdatingStability);

        RuleFor(d => d.CharacterDevelopmentScore)
            .NotNull()
            .WithMessage(ReviewRules.MissingScoreMessage(ReviewRules.CharacterDevelopment))
            .ReviewScore(ReviewRules.CharacterDevelopment);

        RuleFor(d => d.WorldBuildingScore)
            .NotNull()
            .WithMessage(ReviewRules.MissingScoreMessage(ReviewRules.WorldBuilding))
            .ReviewScore(ReviewRules.WorldBuilding);

        RuleFor(d => d.Content)
            .ReviewContent()
            .When(d => !string.IsNullOrWhiteSpace(d.Content));
    }
}
