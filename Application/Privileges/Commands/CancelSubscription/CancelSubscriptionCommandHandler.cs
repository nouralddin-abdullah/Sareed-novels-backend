using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using MediatR;

namespace Application.Privileges.Commands.CancelSubscription;

/// <summary>
/// DELETE /api/novel/{id}/privilege/subscription. The owner's rule (#17): a privilege subscription is a permanent unlock
/// and can't be cancelled, so this always answers 400 <see cref="CannotBeCancelledCode"/>. The route stays so that
/// clients which still offer cancelling get a clear answer.
/// </summary>
public class CancelSubscriptionCommandHandler : IRequestHandler<CancelSubscriptionCommand, OperationResult>
{
    public const string CannotBeCancelledCode = "SubscriptionCannotBeCancelled";
    public const string CannotBeCancelledMessage = "لا يمكن إلغاء الاشتراك في الوصول المبكر، فهو يفتح لك فصول الرواية المقفلة بشكل دائم.";

    public Task<OperationResult> Handle(CancelSubscriptionCommand request, CancellationToken cancellationToken) =>
        throw new BadRequestException(CannotBeCancelledMessage, CannotBeCancelledCode);
}
