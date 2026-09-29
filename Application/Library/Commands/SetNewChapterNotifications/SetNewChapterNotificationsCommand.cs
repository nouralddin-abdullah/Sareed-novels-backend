using FluentValidation;
using MediatR;

namespace Application.Library.Commands.SetNewChapterNotifications;

/// <summary>The body of PATCH /api/library/novel/{novelId}: <c>{ "notifyNewChapters": true | false }</c>.</summary>
public class SetNewChapterNotificationsRequest
{
    /// <summary>Nullable, so that a missing value gets the validator's Arabic message instead of silently false.</summary>
    public bool? NotifyNewChapters { get; set; }
}

public class SetNewChapterNotificationsRequestValidator : AbstractValidator<SetNewChapterNotificationsRequest>
{
    public SetNewChapterNotificationsRequestValidator()
    {
        RuleFor(r => r.NotifyNewChapters)
            .NotNull()
            .WithMessage("حدّد هل تصلك إشعارات الفصول الجديدة لهذه الرواية: notifyNewChapters هي true أو false");
    }
}

/// <summary>
/// PATCH /api/library/novel/{novelId} (#33): turns one library novel's new-chapter notifications (in the app and by
/// push) on or off for the caller. Her other novels and her push preferences are unchanged. 404
/// <see cref="SetNewChapterNotificationsCommandHandler.NotInLibraryCode"/> when the novel isn't in her library.
/// </summary>
public class SetNewChapterNotificationsCommand(Guid novelId, bool notifyNewChapters) : IRequest
{
    public Guid NovelId { get; } = novelId;
    public bool NotifyNewChapters { get; } = notifyNewChapters;
}
