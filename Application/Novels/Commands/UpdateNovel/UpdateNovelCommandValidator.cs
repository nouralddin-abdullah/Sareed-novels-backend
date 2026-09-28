using Domain.Constants;
using FluentValidation;

namespace Application.Novels.Commands.UpdateNovel
{
    public class UpdateNovelCommandValidator : AbstractValidator<UpdateNovelCommandRequest>
    {
        private static readonly string[] AllowedStatuses = { "Ongoing", "Completed" };
        public UpdateNovelCommandValidator()
        {
            RuleFor(x => x.Title)
                .Length(4, 40)
                .When(x => x.Title != null)
                .WithMessage("يجب أن يكون عنوان الرواية من 4 إلى 40 حرفًا");

            RuleFor(x => x.Summary)
            .Length(4, 2000)
            .When(x => x.Summary != null)
            .WithMessage("يجب أن تكون نبذة الرواية من 4 إلى 2000 حرف");

            RuleFor(x => x.Status)
                .Must(status => AllowedStatuses.Contains(status))
                .When(x => x.Status != null)
                .WithMessage("حالة الرواية يجب أن تكون «مستمرة» أو «مكتملة»");

            RuleFor(x => x.GenreIds)
                .Must(genres => genres!.Count >= 1 && genres.Count <= 4)
                .When(x => x.GenreIds != null)
                .WithMessage("اختر من 1 إلى 4 تصنيفات")
                .Must(genres => genres!.Distinct().Count() == genres!.Count)
                .When(x => x.GenreIds != null)
                .WithMessage("اختر كل تصنيف مرة واحدة فقط");

        }
    }
}
