using Application.Validation;
using FluentValidation;

namespace Application.Novels.Commands.ChangeCover;

public class ChangeCoverCommandValidator : AbstractValidator<ChangerCoverCommand>
{
    public ChangeCoverCommandValidator()
    {
        RuleFor(dto => dto.CoverImageUrl)
                .NotNull()
                .Must(ImageValidationUtils.IsValidImageFile)
                .When(dto => dto.CoverImageUrl != null)
                .WithMessage("Profile photo must be a valid image file (JPEG, PNG, WebP) and less than 5MB");
    }

}
