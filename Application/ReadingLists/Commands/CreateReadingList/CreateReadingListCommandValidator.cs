using Application.Validation;
using FluentValidation;

namespace Application.ReadingLists.Commands.CreateReadingList;

public class CreateReadingListCommandValidator : AbstractValidator<CreateReadingListCommand>
{
    public CreateReadingListCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Reading list name is required")
            .Length(1, 100)
            .WithMessage("Reading list name must be between 1 and 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .WithMessage("Reading list description cannot exceed 1000 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        // Validate image file if provided
        RuleFor(x => x.CoverImage)
            .Must(ImageValidationUtils.IsValidImageFile)
            .WithMessage("Invalid image file. Allowed types: JPEG, PNG, WebP. Max size: 5MB")
            .When(x => x.CoverImage != null);
    }
}
