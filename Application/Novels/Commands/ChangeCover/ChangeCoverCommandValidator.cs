using Application.Novels.Commands.CreateNovel;
using Application.Validation;
using FluentValidation;

namespace Application.Novels.Commands.ChangeCover;

public class ChangeCoverCommandValidator : AbstractValidator<ChangeCoverCommandRequest>
{
    public ChangeCoverCommandValidator()
    {
        RuleFor(dto => dto.CoverUrl)
            .NotNull()
            .WithMessage(CreateNovelCommandValidator.CoverRequiredMessage);

        RuleFor(dto => dto.CoverUrl)
            .Must(ImageValidationUtils.IsValidImageFile)
            .When(dto => dto.CoverUrl != null)
            .WithMessage(CreateNovelCommandValidator.CoverInvalidMessage);
    }
}
