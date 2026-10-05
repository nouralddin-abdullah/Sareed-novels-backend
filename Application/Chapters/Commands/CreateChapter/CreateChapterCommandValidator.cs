using Domain.Constants;
using FluentValidation;

namespace Application.Chapters.Commands.CreateChapter
{
    public class CreateChapterCommandValidator : AbstractValidator<CreateChapterRequest>
    {
        public CreateChapterCommandValidator()
        {
            RuleFor(c => c.Title)
                .NotNull()
                .WithMessage(ChapterTextRules.TitleMissingMessage)
                .ChapterTitle();

            RuleFor(c => c.Content)
                .NotNull()
                .WithMessage(ChapterTextRules.ContentMissingMessage)
                .ChapterContent();

            // Anything else is stored as-is and the chapter silently never shows to readers.
            RuleFor(c => c.Status)
                .Must(status => ChapterStatuses.All.Contains(status))
                .WithMessage("حالة الفصل يجب أن تكون «مسودة» أو «منشور»");
        }
    }
}
