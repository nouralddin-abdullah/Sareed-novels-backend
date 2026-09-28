using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Commands.MarkAllAsRead;

public class MarkAllNotificationsAsReadCommandHandler(
    ILogger<MarkAllNotificationsAsReadCommandHandler> logger,
    INotificationsRepository notificationsRepository,
    IUserContext userContext) : IRequestHandler<MarkAllNotificationsAsReadCommand, int>
{
    public async Task<int> Handle(MarkAllNotificationsAsReadCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in", "NotSignedIn");

        // Nothing unread is a success too: the answer is the same however often it is asked (it used to be a 400).
        var marked = await notificationsRepository.MarkAllAsRead(currentUser.Id);
        logger.LogInformation("Marked {Count} notifications read for user {UserId}", marked, currentUser.Id);
        return marked;
    }
}
