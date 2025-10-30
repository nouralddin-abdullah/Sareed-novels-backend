using Application.Validation;
using FluentValidation;

namespace Application.Novels.Commands.ChangeCover;

public class ChangeCoverCommandValidator : AbstractValidator<ChangeCoverCommandRequest>
{
    public ChangeCoverCommandValidator()
    {
        RuleFor(dto => dto.CoverUrl)
                .NotNull()
                .Must(ImageValidationUtils.IsValidImageFile)
                .When(dto => dto.CoverUrl != null)
                .WithMessage("Profile photo must be a valid image file (JPEG, PNG, WebP) and less than 5MB");
    }

}
