using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Wallet.Commands.RejectWithdrawal;

public class RejectWithdrawalCommandHandler(
    ILogger<RejectWithdrawalCommandHandler> logger,
    IUserContext userContext,
    IWithdrawalRequestRepository withdrawalRepository) : IRequestHandler<RejectWithdrawalCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RejectWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var withdrawalRequest = await withdrawalRepository.GetByIdAsync(request.RequestId);
        if (withdrawalRequest == null)
        {
            return new OperationResult
            {
                Success = false,
                Code = "RequestNotFound",
                Message = "طلب السحب غير موجود"
            };
        }

        if (withdrawalRequest.Status != RequestStatus.Pending)
        {
            return new OperationResult
            {
                Success = false,
                Code = "AlreadyProcessed",
                Message = RequestMessages.AlreadyDecided(withdrawalRequest.Status,
                    WithdrawalMessages.IsCancelledByOwner(withdrawalRequest.Status, withdrawalRequest.ProcessedBy, withdrawalRequest.UserId))
            };
        }

        if (string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return new OperationResult
            {
                Success = false,
                Code = "RejectionReasonRequired",
                Message = "اكتب سبب الرفض"
            };
        }

        if (!await withdrawalRepository.TryMarkProcessedAsync(withdrawalRequest.Id, RequestStatus.Rejected, currentUser.Id, request.RejectionReason))
        {
            return new OperationResult
            {
                Success = false,
                Code = "AlreadyProcessed",
                Message = RequestMessages.AlreadyProcessed
            };
        }

        logger.LogInformation(
            "Admin {AdminId} rejected withdrawal {RequestId} for user {UserId}. Reason: {Reason}",
            currentUser.Id, request.RequestId, withdrawalRequest.UserId, request.RejectionReason
        );

        return new OperationResult
        {
            Success = true,
            Message = "رُفض طلب السحب"
        };
    }
}
