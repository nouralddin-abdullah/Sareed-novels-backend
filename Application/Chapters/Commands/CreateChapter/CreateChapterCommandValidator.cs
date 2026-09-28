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
                .WithMessage("اكتب عنوان الفصل")
                .MaximumLength(50)
                .WithMessage("يجب ألا يتجاوز عنوان الفصل 50 حرفًا");

            RuleFor(c => c.Content)
                .NotNull()
                .WithMessage("اكتب نص الفصل")
                .MaximumLength(100000)
                .WithMessage("يجب ألا يتجاوز نص الفصل 100000 حرف");

            // Anything else is stored as-is and the chapter silently never shows to readers.
            RuleFor(c => c.Status)
                .Must(status => ChapterStatuses.All.Contains(status))
                .WithMessage("حالة الفصل يجب أن تكون «مسودة» أو «منشور»");
        }
    }
}
