using FluentValidation;

namespace Application.Reviews.Commands.CreateReview;

public class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator()
    {
        RuleFor(d => d.WritingQualityScore)
            .NotNull()
            .WithMessage("Writing Quality Score is required")
            .InclusiveBetween(1.0m, 5.0m)
            .WithMessage("Writing Quality Score must be between 1.0 and 5.0");

        RuleFor(d => d.UpdatingStabilityScore)
            .NotNull()
            .WithMessage("Updating Stability Score is required")
            .InclusiveBetween(1.0m, 5.0m)
            .WithMessage("Updating Stability Score must be between 1.0 and 5.0");

        RuleFor(d => d.CharacterDevelopmentScore)
            .NotNull()
            .WithMessage("Character Development Score is required")
            .InclusiveBetween(1.0m, 5.0m)
            .WithMessage("Character Development Score must be between 1.0 and 5.0");

        RuleFor(d => d.WorldBuildingScore)
            .NotNull()
            .WithMessage("World Building Score is required")
            .InclusiveBetween(1.0m, 5.0m)
            .WithMessage("World Building Score must be between 1.0 and 5.0");

        RuleFor(d => d.Content)
            .MaximumLength(2000)
            .WithMessage("Review content cannot exceed 2000 characters")
            .Must(content => string.IsNullOrWhiteSpace(content) || content.Trim().Length >= 5)
            .WithMessage("Review content must be at least 10 characters long when provided")
            .When(d => !string.IsNullOrWhiteSpace(d.Content));
    }
}
