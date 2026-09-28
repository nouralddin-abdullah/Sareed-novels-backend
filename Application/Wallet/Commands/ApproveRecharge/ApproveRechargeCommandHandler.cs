using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Wallet.Commands.ApproveRecharge;

public class ApproveRechargeCommandHandler(
    ILogger<ApproveRechargeCommandHandler> logger,
    IUserContext userContext,
    IRechargeRequestRepository rechargeRepository,
    IWalletService walletService,
    ITransactionManager transactionManager) : IRequestHandler<ApproveRechargeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(ApproveRechargeCommand request, CancellationToken cancellationToken)
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

        // The status change and the credit commit together, and the status only changes if the request is still
        // Pending, so a second approval (double click, two admins) can't credit the points twice.
        var approved = await transactionManager.InTransactionAsync(async () =>
        {
            if (!await rechargeRepository.TryMarkProcessedAsync(rechargeRequest.Id, RequestStatus.Approved, currentUser.Id))
            {
                return false;
            }

            await walletService.AddPointsAsync(
                rechargeRequest.UserId,
                rechargeRequest.PointsRequested,
                TransactionType.RechargeApproved,
                TransactionDescriptions.RechargeApproved(rechargeRequest.PointsRequested, rechargeRequest.TotalAmountEGP, rechargeRequest.PaymentMethod),
                rechargeRequest.Id
            );
            return true;
        }, cancellationToken);

        if (!approved)
        {
            return new OperationResult
            {
                Success = false,
                Code = "AlreadyProcessed",
                Message = RequestMessages.AlreadyProcessed
            };
        }

        logger.LogInformation(
            "Admin {AdminId} approved recharge {RequestId} for user {UserId}: {Points} points",
            currentUser.Id, request.RequestId, rechargeRequest.UserId, rechargeRequest.PointsRequested
        );

        return new OperationResult
        {
            Success = true,
            Message = $"قُبل طلب الشحن، وأُضيفت {rechargeRequest.PointsRequested} نقطة إلى محفظة المستخدم."
        };
    }
}
