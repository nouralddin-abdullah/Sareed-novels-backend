using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Library.Commands.RemoveFromLibrary;

public class RemoveFromLibraryCommandHandler(
    ILogger<RemoveFromLibraryCommandHandler> logger,
    ILibraryRepository libraryRepository,
    IUserContext userContext) : IRequestHandler<RemoveFromLibraryCommand, bool>
{
    public async Task<bool> Handle(RemoveFromLibraryCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var removed = await libraryRepository.RemoveFromLibraryAsync(currentUser.Id, request.NovelId);

        logger.LogInformation(
            removed
                ? "User {UserId} removed novel {NovelId} from their library"
                : "User {UserId} removed novel {NovelId} from their library, where it wasn't: nothing to do",
            currentUser.Id, request.NovelId);

        return removed;
    }
}
