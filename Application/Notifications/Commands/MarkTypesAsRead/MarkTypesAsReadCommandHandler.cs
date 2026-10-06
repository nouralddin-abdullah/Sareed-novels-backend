using Application.Common;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Commands.MarkTypesAsRead;

/// <summary>
/// Marks only the types named (#92). Unlike a list's filter, naming no known type never means every type: it marks
/// nothing, since those types don't exist here. Types that name nothing at all are refused; PATCH read-all marks every type.
/// </summary>
public class MarkTypesAsReadCommandHandler(
    ILogger<MarkTypesAsReadCommandHandler> logger,
    INotificationsRepository notificationsRepository,
    IUserContext userContext) : IRequestHandler<MarkTypesAsReadCommand, MarkTypesAsReadResult>
{
    public async Task<MarkTypesAsReadResult> Handle(MarkTypesAsReadCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        if (!NameFilter.NamesAny(request.Types))
        {
            throw new BadRequestException(MarkTypesAsReadCommand.TypesRequiredMessage, MarkTypesAsReadCommand.ValidationFailed);
        }

        var types = NotificationTypeFilter.Exactly(request.Types);
        var marked = await notificationsRepository.MarkTypesAsRead(currentUser.Id, types);
        logger.LogInformation("Marked {Count} notifications of types {Types} read for user {UserId}", marked,
            types.Count == 0 ? "none known" : string.Join(",", types), currentUser.Id);

        return new MarkTypesAsReadResult
        {
            Marked = marked,
            UnreadCount = await notificationsRepository.GetUnreadCount(currentUser.Id),
            TypesUnreadCount = types.Count == 0 ? 0 : await notificationsRepository.GetUnreadCount(currentUser.Id, types)
        };
    }
}
