using FluentValidation;

namespace Application.Chapters.Commands.UpdateChapter
{
    public class UpdateChapterValidator : AbstractValidator<UpdateChapterRequest>
    {
        public UpdateChapterValidator()
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
