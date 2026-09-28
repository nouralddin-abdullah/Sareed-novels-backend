using Application.Users;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Wallet.Commands.CancelWithdrawal;

/// <summary>
/// A member cancels their own pending withdrawal request (#27): DELETE /api/wallet/withdraw/{id}. A pending request
/// reserves its points so they can't be asked for twice, and one that can no longer be paid (made before #22 against
/// bought points, say) used to lock its owner out of withdrawing what they earned since, until an admin rejected it.
/// Nothing was deducted for it yet, so cancelling only closes it.
/// </summary>
public sealed record CancelWithdrawalCommand(Guid RequestId) : IRequest;

/// <summary>
/// Closes the request as Rejected with the reason «ألغاه صاحب الطلب» and the member as ProcessedBy, the way an account
/// deletion cancels a member's requests, so every app shows it as it already shows those. Cancelling a request the member
/// cancelled already changes nothing and succeeds too (a repeat after a lost answer is done, as with the other idempotent
/// writes, #25). 404 RequestNotFound for an unknown id or another member's request (whether it exists isn't theirs to
/// know); 409 AlreadyProcessed once an admin approved or rejected it.
/// </summary>
public class CancelWithdrawalCommandHandler(
    ILogger<CancelWithdrawalCommandHandler> logger,
    IUserContext userContext,
    IWithdrawalRequestRepository withdrawals) : IRequestHandler<CancelWithdrawalCommand>
{
    public const string NotFoundCode = "RequestNotFound";
    public const string AlreadyProcessedCode = "AlreadyProcessed";
    public const string NotFoundMessage = "طلب السحب غير موجود";

    public async Task Handle(CancelWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var user = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var state = await withdrawals.GetStateAsync(request.RequestId);
        if (state is not { } found || found.UserId != user.Id)
        {
            throw new NotFoundException(NotFoundMessage, NotFoundCode);
        }

        // One conditional UPDATE: a double tap, or an admin deciding it at the same moment, changes it once.
        if (found.Status == RequestStatus.Pending
            && await withdrawals.TryMarkProcessedAsync(request.RequestId, RequestStatus.Rejected, user.Id, WithdrawalMessages.CancelledByOwnerReason))
        {
            logger.LogInformation("User {UserId} cancelled their withdrawal request {RequestId}", user.Id, request.RequestId);
            return;
        }

        var now = found.Status == RequestStatus.Pending ? await withdrawals.GetStateAsync(request.RequestId) ?? found : found;
        if (WithdrawalMessages.IsCancelledByOwner(now.Status, now.ProcessedBy, user.Id))
        {
            return; // cancelled already: nothing left to do
        }
        throw new ConflictException(WithdrawalMessages.NotCancellable(now.Status), AlreadyProcessedCode);
    }
}
