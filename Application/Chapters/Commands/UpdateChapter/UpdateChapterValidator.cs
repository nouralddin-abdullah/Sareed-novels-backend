using Domain.Constants;
using FluentValidation;

namespace Application.Chapters.Commands.UpdateChapter
{
    public class UpdateChapterValidator : AbstractValidator<UpdateChapterRequest>
    {
        public UpdateChapterValidator()
        {
            RuleFor(c => c.Title)
                .NotNull()
                .WithMessage(ChapterTextRules.TitleMissingMessage)
                .ChapterTitle();

            RuleFor(c => c.Content)
                .NotNull()
                .WithMessage(ChapterTextRules.ContentMissingMessage)
                .ChapterContent();

            RuleFor(c => c.Status)
                .Must(status => ChapterStatuses.All.Contains(status))
                .When(c => c.Status != null)
                .WithMessage("حالة الفصل يجب أن تكون «مسودة» أو «منشور»");
        }
    }
}
