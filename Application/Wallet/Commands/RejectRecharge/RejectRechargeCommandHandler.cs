using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Wallet.Commands.RejectRecharge;

public class RejectRechargeCommandHandler(
    ILogger<RejectRechargeCommandHandler> logger,
    IUserContext userContext,
    IRechargeRequestRepository rechargeRepository) : IRequestHandler<RejectRechargeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RejectRechargeCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var rechargeRequest = await rechargeRepository.GetByIdAsync(request.RequestId);
        if (rechargeRequest == null)
        {
            return new OperationResult
            {
                Success = false,
                Code = "RequestNotFound",
                Message = "طلب الشحن غير موجود"
            };
        }

        if (rechargeRequest.Status != RequestStatus.Pending)
        {
            return new OperationResult
            {
                Success = false,
                Code = "AlreadyProcessed",
                Message = RequestMessages.AlreadyDecided(rechargeRequest.Status)
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

        if (!await rechargeRepository.TryMarkProcessedAsync(rechargeRequest.Id, RequestStatus.Rejected, currentUser.Id, request.RejectionReason))
        {
            return new OperationResult
            {
                Success = false,
                Code = "AlreadyProcessed",
                Message = RequestMessages.AlreadyProcessed
            };
        }

        logger.LogInformation(
            "Admin {AdminId} rejected recharge {RequestId} for user {UserId}. Reason: {Reason}",
            currentUser.Id, request.RequestId, rechargeRequest.UserId, request.RejectionReason
        );

        return new OperationResult
        {
            Success = true,
            Message = "رُفض طلب الشحن"
        };
    }
}
