using Application.Validation;
using FluentValidation;

namespace Application.Entities.Commands.AddGalleryImage;

public class AddGalleryImageCommandValidator : AbstractValidator<AddGalleryImageCommand>
{
    public AddGalleryImageCommandValidator()
    {
        RuleFor(x => x.ImageFile)
            .NotNull()
            .WithMessage("Image file is required")
            .Must(ImageValidationUtils.IsValidImageFile)
            .WithMessage("Image must be a valid image file (JPEG, PNG, WebP) and less than 5MB");

        RuleFor(x => x.Caption)
            .MaximumLength(500)
            .When(x => x.Caption != null)
            .WithMessage("Caption must not exceed 500 characters");

        RuleFor(x => x.OrderIndex)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Order index must be 0 or greater");
    }
}
