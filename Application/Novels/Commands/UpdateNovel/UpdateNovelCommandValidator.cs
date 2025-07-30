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
                .WithMessage("Title must be between 4 and 40 characters");

            RuleFor(x => x.Summary)
            .Length(4, 500)
            .When(x => x.Summary != null)
            .WithMessage("Summary must be between 4 and 500 characters");

            RuleFor(x => x.Status)
                .Must(status => AllowedStatuses.Contains(status))
                .When(x => x.Status != null)
                .WithMessage("Status must be either 'Ongoing' or 'Completed'");

        }
    }
}
