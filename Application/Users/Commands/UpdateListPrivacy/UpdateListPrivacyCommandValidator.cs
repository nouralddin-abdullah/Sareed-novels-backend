using Domain.Moderation;
using Domain.Profiles;
using FluentValidation;

namespace Application.Users.Commands.UpdateListPrivacy;

/// <summary>
/// A value sent is one of <see cref="ListVisibility"/>'s names, in any letter case; anything else is refused with 400
/// ValidationFailed, the error under the field's name.
/// </summary>
public class UpdateListPrivacyCommandValidator : AbstractValidator<UpdateListPrivacyCommand>
{
    public const string ReviewsMessage = "قيمة ظهور المراجعات غير صالحة. القيم الممكنة: Everyone أو OnlyMe";
    public const string CommentsMessage = "قيمة ظهور التعليقات غير صالحة. القيم الممكنة: Everyone أو OnlyMe";

    public UpdateListPrivacyCommandValidator()
    {
        RuleFor(command => command.Reviews)
            .Must(value => EnumNames.TryParse<ListVisibility>(value, out _))
            .When(command => command.Reviews is not null)
            .WithMessage(ReviewsMessage);

        RuleFor(command => command.Comments)
            .Must(value => EnumNames.TryParse<ListVisibility>(value, out _))
            .When(command => command.Comments is not null)
            .WithMessage(CommentsMessage);
    }
}
