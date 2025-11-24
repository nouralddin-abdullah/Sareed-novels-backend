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
    IWalletService walletService) : IRequestHandler<ApproveRechargeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(ApproveRechargeCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

        // TODO: Add role check for Admin
        // For now, assuming authentication is handled by [Authorize(Roles = "Admin")] on controller

        var rechargeRequest = await rechargeRepository.GetByIdAsync(request.RequestId);
        if (rechargeRequest == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Recharge request not found"
            };
        }

        if (rechargeRequest.Status != RequestStatus.Pending)
        {
            return new OperationResult
            {
                Success = false,
                Message = $"Request already {rechargeRequest.Status.ToLower()}"
            };
        }

        // Update request status
        rechargeRequest.Status = RequestStatus.Approved;
        rechargeRequest.ProcessedAt = DateTime.UtcNow;
        rechargeRequest.ProcessedBy = currentUser.Id;

        await rechargeRepository.UpdateAsync(rechargeRequest);

        // Add points to user wallet
        await walletService.AddPointsAsync(
            rechargeRequest.UserId,
            rechargeRequest.PointsRequested,
            TransactionType.RechargeApproved,
            $"Recharge approved: {rechargeRequest.PointsRequested} points ({rechargeRequest.TotalAmountEGP} EGP via {rechargeRequest.PaymentMethod})",
            rechargeRequest.Id
        );

        logger.LogInformation(
            "Admin {AdminId} approved recharge {RequestId} for user {UserId}: {Points} points",
            currentUser.Id, request.RequestId, rechargeRequest.UserId, rechargeRequest.PointsRequested
        );

        return new OperationResult
        {
            Success = true,
            Message = $"Recharge request approved. {rechargeRequest.PointsRequested} points added to user wallet."
        };
    }
}
