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
            .Length(4, 2000)
            .When(x => x.Summary != null)
            .WithMessage("Summary must be between 4 and 500 characters");

            RuleFor(x => x.Status)
                .Must(status => AllowedStatuses.Contains(status))
                .When(x => x.Status != null)
                .WithMessage("Status must be either 'Ongoing' or 'Completed'");

            RuleFor(x => x.GenreIds)
                .Must(genres => genres!.Count >= 1 && genres.Count <= 4)
                .When(x => x.GenreIds != null)
                .WithMessage("A novel must have between 1 and 4 genres")
                .Must(genres => genres!.Distinct().Count() == genres!.Count)
                .When(x => x.GenreIds != null)
                .WithMessage("A genre can only be selected once");

        }
    }
}
