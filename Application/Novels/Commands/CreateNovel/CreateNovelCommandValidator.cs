using System.Data;
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
            RuleFor(dto => dto.Title)
            .NotEmpty()
            .WithMessage("اكتب عنوان الرواية")
            .Length(4, 40)
            .WithMessage("يجب أن يكون عنوان الرواية من 4 إلى 40 حرفًا");

            RuleFor(dto => dto.Summary)
            .NotEmpty()
            .WithMessage("اكتب نبذة الرواية")
            .Length(4, 2000)
            .WithMessage("يجب أن تكون نبذة الرواية من 4 إلى 2000 حرف");

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
            .WithMessage("اختر تصنيفًا واحدًا على الأقل")
            .Must(genres => genres.Count >= 1 && genres.Count <= 4)
            .WithMessage("اختر من 1 إلى 4 تصنيفات")
            .Must(genres => genres.Distinct().Count() == genres.Count)
            .WithMessage("اختر كل تصنيف مرة واحدة فقط");

        }
    }
}
