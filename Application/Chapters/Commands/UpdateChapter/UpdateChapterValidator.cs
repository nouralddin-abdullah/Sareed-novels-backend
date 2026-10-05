using Domain.Constants;
using FluentValidation;

namespace Application.Chapters.Commands.UpdateChapter
{
    public class UpdateChapterValidator : AbstractValidator<UpdateChapterRequest>
    {
        /// <summary>A request with neither a status nor a title and text: nothing to save.</summary>
        public const string StatusMissingMessage = "أرسل حالة الفصل، أو عنوانه ونصه";

        public UpdateChapterValidator()
        {
            // A change of status alone leaves the title and the text out together (#75); either one sent needs the
            // other, as before, and both keep their limits.
            RuleFor(c => c.Title)
                .NotNull()
                .WithMessage(ChapterTextRules.TitleMissingMessage)
                .When(c => c.Content != null);
            RuleFor(c => c.Title).ChapterTitle();

            RuleFor(c => c.Content)
                .NotNull()
                .WithMessage(ChapterTextRules.ContentMissingMessage)
                .When(c => c.Title != null);
            RuleFor(c => c.Content).ChapterContent();

            RuleFor(c => c.Status)
                .NotNull()
                .WithMessage(StatusMissingMessage)
                .When(c => c.Title == null && c.Content == null);

            RuleFor(c => c.Status)
                .Must(status => ChapterStatuses.All.Contains(status))
                .When(c => c.Status != null)
                .WithMessage("حالة الفصل يجب أن تكون «مسودة» أو «منشور»");
        }
    }
}
