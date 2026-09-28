using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using MediatR;

namespace Application.Privileges.Commands.CancelSubscription;

/// <summary>
/// DELETE /api/novel/{id}/privilege/subscription. The owner's rule (#17): a privilege subscription is a permanent unlock
/// and can't be cancelled, so a subscriber is answered 400 <see cref="CannotBeCancelledCode"/>. Without a (still active)
/// subscription there is nothing to cancel: the state is already as asked, 204 like the other idempotent writes (#25).
/// The route stays so that clients which still offer cancelling get a clear answer.
/// </summary>
public class CancelSubscriptionCommandHandler(IUserContext userContext, IPrivilegeService privilegeService)
    : IRequestHandler<CancelSubscriptionCommand, OperationResult>
{
    public const string CannotBeCancelledCode = "SubscriptionCannotBeCancelled";
    public const string CannotBeCancelledMessage = "لا يمكن إلغاء الاشتراك في الوصول المبكر، فهو يفتح لك فصول الرواية المقفلة بشكل دائم.";

    public async Task<OperationResult> Handle(CancelSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        if (!await privilegeService.HasActiveSubscriptionAsync(request.NovelId, currentUser.Id))
        {
            return OperationResult.AlreadyDone("NotSubscribed", "لست مشتركًا في الوصول المبكر لهذه الرواية");
        }

        throw new BadRequestException(CannotBeCancelledMessage, CannotBeCancelledCode);
    }
}
