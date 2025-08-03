using System.Data;
using Application.Validation;
using FluentValidation;

namespace Application.Novels.Commands.CreateNovel
{
    public class CreateNovelCommandValidator : AbstractValidator<CreateNovelCommand>
    {
        public CreateNovelCommandValidator()
        {
            RuleFor(dto => dto.Title)
            .Length(4, 40)
            .NotNull()
            .NotEmpty()
            .WithMessage("A novel should have valid title");

            RuleFor(dto => dto.Summary)
            .Length(4, 500)
            .NotNull()
            .NotEmpty()
            .WithMessage("A novel should have valid Summary");

            RuleFor(dto => dto.CoverImageUrl)
                .NotNull()
                .Must(ImageValidationUtils.IsValidImageFile)
                .When(dto => dto.CoverImageUrl != null)
                .WithMessage("Profile photo must be a valid image file (JPEG, PNG, WebP) and less than 5MB");

            RuleFor(x => x.GenreIds)
            .NotEmpty()
            .WithMessage("At least one genre is required")
            .Must(genres => genres.Count >= 1 && genres.Count <= 4)
            .WithMessage("A novel must have between 1 and 4 genres");

        }
    }
}
