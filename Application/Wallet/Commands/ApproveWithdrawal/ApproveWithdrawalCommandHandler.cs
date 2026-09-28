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
    IWalletService walletService,
    ITransactionManager transactionManager) : IRequestHandler<ApproveWithdrawalCommand, OperationResult>
{
    public async Task<OperationResult> Handle(ApproveWithdrawalCommand request, CancellationToken cancellationToken)
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
                Message = RequestMessages.AlreadyDecided(withdrawalRequest.Status, withdrawalRequest.RejectionReason)
            };
        }

        // The status change and the debit commit together: if the debit fails the request stays Pending (it used to
        // be left Approved with nothing deducted), and a second approval can't deduct twice. What can be paid is checked
        // again here, not the balance (#22, #27): released earnings the member hasn't spent, which a refund since the
        // request may have taken. The wallet stays locked from that check to the debit, so nothing can change it in between.
        bool approved;
        try
        {
            approved = await transactionManager.InTransactionAsync(async () =>
            {
                var withdrawable = await walletService.GetWithdrawableForUpdateAsync(withdrawalRequest.UserId);

                if (!await withdrawalRepository.TryMarkProcessedAsync(withdrawalRequest.Id, RequestStatus.Approved, currentUser.Id))
                {
                    return false;
                }

                if (withdrawable.Payable < withdrawalRequest.PointsRequested)
                {
                    throw new NotWithdrawableException(withdrawable); // rolls the status change back
                }

                await walletService.DeductPointsAsync(
                    withdrawalRequest.UserId,
                    withdrawalRequest.PointsRequested,
                    TransactionType.WithdrawalApproved,
                    TransactionDescriptions.WithdrawalApproved(withdrawalRequest.PointsRequested, withdrawalRequest.NetAmountEGP, withdrawalRequest.WithdrawalMethod),
                    withdrawalRequest.Id
                );
                return true;
            }, cancellationToken);
        }
        catch (NotWithdrawableException ex)
        {
            logger.LogWarning(
                "Withdrawal {RequestId} of {Points} points not approved: user {UserId} can be paid {Payable} (balance {Balance}: released earnings {Released}, on hold {Held}, bought {Bought}, owed {Deficit})",
                request.RequestId, withdrawalRequest.PointsRequested, withdrawalRequest.UserId, ex.Balance.Payable, ex.Balance.Balance,
                ex.Balance.Released, ex.Balance.PendingEarnings, ex.Balance.Bought, ex.Balance.Deficit);
            return new OperationResult
            {
                Success = false,
                Code = WithdrawalMessages.NotWithdrawableCode,
                Message = WithdrawalMessages.NotPayable(ex.Balance)
            };
        }
        catch (InsufficientBalanceException ex)
        {
            // The locked check above makes this unreachable; kept so a change there can't turn into a 500.
            logger.LogError(ex, "Withdrawal {RequestId} not approved: balance too low after the withdrawable check", request.RequestId);
            return new OperationResult
            {
                Success = false,
                Code = WithdrawalMessages.NotWithdrawableCode,
                Message = "لم يعد رصيد المستخدم يكفي لهذا السحب"
            };
        }

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
            "Admin {AdminId} approved withdrawal {RequestId} for user {UserId}: {Points} points → {NetAmount} EGP",
            currentUser.Id, request.RequestId, withdrawalRequest.UserId, withdrawalRequest.PointsRequested, withdrawalRequest.NetAmountEGP
        );

        return new OperationResult
        {
            Success = true,
            Message = $"قُبل طلب السحب، وخُصمت {withdrawalRequest.PointsRequested} نقطة. يستلم المستخدم {RequestMessages.Egp(withdrawalRequest.NetAmountEGP)} جنيه."
        };
    }

    private sealed class NotWithdrawableException(WithdrawableBalance balance) : Exception
    {
        public WithdrawableBalance Balance { get; } = balance;
    }
}
