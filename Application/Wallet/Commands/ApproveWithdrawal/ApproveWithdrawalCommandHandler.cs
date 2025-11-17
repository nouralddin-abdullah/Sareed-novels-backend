using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Wallet.Commands.ApproveWithdrawal;

public class ApproveWithdrawalCommandHandler(
    ILogger<ApproveWithdrawalCommandHandler> logger,
    IUserContext userContext,
    IWithdrawalRequestRepository withdrawalRepository,
    IWalletService walletService) : IRequestHandler<ApproveWithdrawalCommand, OperationResult>
{
    public async Task<OperationResult> Handle(ApproveWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

        var withdrawalRequest = await withdrawalRepository.GetByIdAsync(request.RequestId);
        if (withdrawalRequest == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Withdrawal request not found"
            };
        }

        if (withdrawalRequest.Status != RequestStatus.Pending)
        {
            return new OperationResult
            {
                Success = false,
                Message = $"Request already {withdrawalRequest.Status.ToLower()}"
            };
        }

        // Check if user still has sufficient balance
        if (!await walletService.HasSufficientBalanceAsync(withdrawalRequest.UserId, withdrawalRequest.PointsRequested))
        {
            return new OperationResult
            {
                Success = false,
                Message = "User no longer has sufficient balance for this withdrawal"
            };
        }

        // Update request status
        withdrawalRequest.Status = RequestStatus.Approved;
        withdrawalRequest.ProcessedAt = DateTime.UtcNow;
        withdrawalRequest.ProcessedBy = currentUser.Id;

        await withdrawalRepository.UpdateAsync(withdrawalRequest);

        // Deduct points from user wallet
        try
        {
            await walletService.DeductPointsAsync(
                withdrawalRequest.UserId,
                withdrawalRequest.PointsRequested,
                TransactionType.Withdrawal,
                $"Withdrawal approved: {withdrawalRequest.PointsRequested} points ({withdrawalRequest.NetAmountEGP} EGP via {withdrawalRequest.WithdrawalMethod})",
                withdrawalRequest.Id
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to deduct points for withdrawal {RequestId}", request.RequestId);
            return new OperationResult
            {
                Success = false,
                Message = ex.Message
            };
        }

        logger.LogInformation(
            "Admin {AdminId} approved withdrawal {RequestId} for user {UserId}: {Points} points → {NetAmount} EGP",
            currentUser.Id, request.RequestId, withdrawalRequest.UserId, withdrawalRequest.PointsRequested, withdrawalRequest.NetAmountEGP
        );

        return new OperationResult
        {
            Success = true,
            Message = $"Withdrawal approved. {withdrawalRequest.PointsRequested} points deducted. User receives {withdrawalRequest.NetAmountEGP} EGP."
        };
    }
}
