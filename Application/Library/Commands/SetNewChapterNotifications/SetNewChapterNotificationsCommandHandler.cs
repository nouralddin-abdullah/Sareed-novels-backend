using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Library.Commands.SetNewChapterNotifications;

public class SetNewChapterNotificationsCommandHandler(
    ILogger<SetNewChapterNotificationsCommandHandler> logger,
    ILibraryRepository libraryRepository,
    IUserContext userContext) : IRequestHandler<SetNewChapterNotificationsCommand>
{
    public const string NotInLibraryCode = "NotInLibrary";
    public const string NotInLibraryMessage = "هذه الرواية ليست في مكتبتك.";

    public async Task Handle(SetNewChapterNotificationsCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        // Setting the value the entry already has is done too (204); only a missing entry is refused.
        if (!await libraryRepository.SetNewChapterNotificationsAsync(currentUser.Id, request.NovelId, request.NotifyNewChapters))
        {
            throw new NotFoundException(NotInLibraryMessage, NotInLibraryCode);
        }

        logger.LogInformation("User {UserId} turned new-chapter notifications {State} for novel {NovelId}",
            currentUser.Id, request.NotifyNewChapters ? "on" : "off", request.NovelId);
    }
}
