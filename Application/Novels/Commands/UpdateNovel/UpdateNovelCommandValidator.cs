using Domain.Constants;
using FluentValidation;

namespace Application.Novels.Commands.UpdateNovel
{
    public class UpdateNovelCommandValidator : AbstractValidator<UpdateNovelCommandRequest>
    {
        private static readonly string[] AllowedStatuses = { "Ongoing", "Completed" };
        public UpdateNovelCommandValidator()
        {
            // A field sent follows the rules of creating a novel, with the same messages (#76): a title or summary of
            // spaces only was saved as is, and an empty genre list had another message. A field left out stays as it is.
            RuleFor(x => x.Title)
                .NovelTitle()
                .When(x => x.Title != null);

            RuleFor(x => x.Summary)
                .NovelSummary()
                .When(x => x.Summary != null);

            RuleFor(x => x.Status)
                .Must(status => AllowedStatuses.Contains(status))
                .When(x => x.Status != null)
                .WithMessage("حالة الرواية يجب أن تكون «مستمرة» أو «مكتملة»");

            RuleFor(x => x.GenreIds)
                .NovelGenres()
                .When(x => x.GenreIds != null);

        }
    }
}
