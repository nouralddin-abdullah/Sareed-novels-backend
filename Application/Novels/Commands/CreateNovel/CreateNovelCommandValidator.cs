using System.Data;
using Application.Validation;
using FluentValidation;

namespace Application.Novels.Commands.CreateNovel
{
    public class CreateNovelCommandValidator : AbstractValidator<CreateNovelCommand>
    {
        public const string CoverRequiredMessage = "A cover image is required (JPEG, PNG or WebP, at most 5 MB).";
        public const string CoverInvalidMessage = "The cover must be a JPEG, PNG or WebP image of at most 5 MB.";

        public CreateNovelCommandValidator()
        {
            RuleFor(dto => dto.Title)
            .Length(4, 40)
            .NotNull()
            .NotEmpty()
            .WithMessage("A novel should have valid title");

            RuleFor(dto => dto.Summary)
            .Length(4, 2000)
            .NotNull()
            .NotEmpty()
            .WithMessage("A novel should have valid Summary");

            // Every novel is stored with a cover (Novel.CoverImageUrl is NOT NULL), so a missing file is a 400,
            // not a failed insert. The two rules are separate so the null check isn't switched off by a When().
            RuleFor(dto => dto.CoverImageUrl)
                .NotNull()
                .WithMessage(CoverRequiredMessage);

            RuleFor(dto => dto.CoverImageUrl)
                .Must(ImageValidationUtils.IsValidImageFile)
                .When(dto => dto.CoverImageUrl != null)
                .WithMessage(CoverInvalidMessage);

            RuleFor(x => x.GenreIds)
            .NotEmpty()
            .WithMessage("At least one genre is required")
            .Must(genres => genres.Count >= 1 && genres.Count <= 4)
            .WithMessage("A novel must have between 1 and 4 genres")
            .Must(genres => genres.Distinct().Count() == genres.Count)
            .WithMessage("A genre can only be selected once");

        }
    }
}
