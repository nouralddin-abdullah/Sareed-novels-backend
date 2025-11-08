using Application.Validation;
using FluentValidation;

namespace Application.ReadingLists.Commands.UpdateReadingList;

public class UpdateReadingListCommandValidator : AbstractValidator<UpdateReadingListCommand>
{
    public UpdateReadingListCommandValidator()
    {
        RuleFor(x => x.Name)
            .Length(1, 100)
            .WithMessage("Reading list name must be between 1 and 100 characters")
            .When(x => x.Name != null);

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .WithMessage("Description cannot exceed 1000 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.CoverImage)
            .Must(ImageValidationUtils.IsValidImageFile)
            .WithMessage("Invalid image file. Allowed types: JPEG, PNG, WebP. Max size: 5MB")
            .When(x => x.CoverImage != null);
    }
}
