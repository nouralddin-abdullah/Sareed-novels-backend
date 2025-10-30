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
        }
    }
}
