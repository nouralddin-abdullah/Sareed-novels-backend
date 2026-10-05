using Application.Validation;
using FluentValidation;

namespace Application.Novels.Commands.CreateNovel
{
    public class CreateNovelCommandValidator : AbstractValidator<CreateNovelCommand>
    {
        public const string CoverRequiredMessage = "صورة الغلاف مطلوبة (JPEG أو PNG أو WebP، بحد أقصى 5 ميغابايت).";
        public const string CoverInvalidMessage = "يجب أن يكون الغلاف صورة JPEG أو PNG أو WebP لا يتجاوز حجمها 5 ميغابايت.";

        public CreateNovelCommandValidator()
        {
            // The same rules and messages as an edit (UpdateNovelCommandValidator, #76).
            RuleFor(dto => dto.Title).NovelTitle();

            RuleFor(dto => dto.Summary).NovelSummary();

            // Every novel is stored with a cover (Novel.CoverImageUrl is NOT NULL), so a missing file is a 400,
            // not a failed insert. The two rules are separate so the null check isn't switched off by a When().
            RuleFor(dto => dto.CoverImageUrl)
                .NotNull()
                .WithMessage(CoverRequiredMessage);

            RuleFor(dto => dto.CoverImageUrl)
                .Must(ImageValidationUtils.IsValidImageFile)
                .When(dto => dto.CoverImageUrl != null)
                .WithMessage(CoverInvalidMessage);

            RuleFor(x => x.GenreIds).NovelGenres();

        }
    }
}
