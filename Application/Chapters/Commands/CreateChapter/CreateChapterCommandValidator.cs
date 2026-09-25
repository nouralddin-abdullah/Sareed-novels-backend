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
                .MaximumLength(50)
                .WithMessage("Chapter title shouldn't be null, and max length is 50");

            RuleFor(c => c.Content)
                .NotNull()
                .MaximumLength(100000);

            // Anything else is stored as-is and the chapter silently never shows to readers.
            RuleFor(c => c.Status)
                .Must(status => ChapterStatuses.All.Contains(status))
                .WithMessage("Chapter status must be 'Draft' or 'Published'");
        }
    }
}
