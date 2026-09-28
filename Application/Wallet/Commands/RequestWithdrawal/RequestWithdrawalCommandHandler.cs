using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Wallet.Commands.RequestWithdrawal;

public class RequestWithdrawalCommandHandler(
    ILogger<RequestWithdrawalCommandHandler> logger,
    IUserContext userContext,
    IWithdrawalRequestRepository withdrawalRepository,
    IPointCalculationService calculationService,
    IWalletService walletService,
    ITransactionManager transactionManager) : IRequestHandler<RequestWithdrawalCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RequestWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        // Validate minimum points
        if (request.PointsRequested < PointsConstants.MinimumWithdrawal)
        {
            return new OperationResult
            {
                Success = false,
                Code = "BelowMinimumWithdrawal",
                Message = $"الحد الأدنى للسحب {PointsConstants.MinimumWithdrawal} نقطة"
            };
        }

        // Validate withdrawal method
        if (request.WithdrawalMethod != Domain.Constants.PaymentMethod.VodafoneCash &&
            request.WithdrawalMethod != Domain.Constants.PaymentMethod.InstaPay &&
            request.WithdrawalMethod != Domain.Constants.PaymentMethod.PayPal)
        {
            return new OperationResult
            {
                Success = false,
                Code = "InvalidPaymentMethod",
                Message = "طريقة السحب غير صالحة. اختر فودافون كاش أو إنستاباي أو باي بال"
            };
        }

        // Validate payment details
        if (string.IsNullOrWhiteSpace(request.PaymentDetails))
        {
            return new OperationResult
            {
                Success = false,
                Code = "PaymentDetailsRequired",
                Message = "اكتب بيانات الاستلام: رقم الهاتف أو البريد الإلكتروني"
            };
        }

        // Calculate amounts
        var (baseAmount, tax, netAmount) = calculationService.CalculateWithdrawalNet(request.PointsRequested);

        // Create withdrawal request
        var withdrawalRequest = new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            UserId = currentUser.Id,
            PointsRequested = request.PointsRequested,
            BaseAmountEGP = baseAmount,
            TaxDeducted = tax,
            NetAmountEGP = netAmount,
            WithdrawalMethod = request.WithdrawalMethod,
            PaymentDetails = request.PaymentDetails,
            Status = RequestStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };

        // Only withdrawable points (released earnings, #22), not the balance. Checked and saved while the wallet is
        // locked, so two requests at once can't both count the same points: the second one sees the first as pending.
        var refusal = await transactionManager.InTransactionAsync<WithdrawableBalance?>(async () =>
        {
            var withdrawable = await walletService.GetWithdrawableForUpdateAsync(currentUser.Id);
            if (withdrawable.Withdrawable < request.PointsRequested)
            {
                return withdrawable;
            }

            await withdrawalRepository.CreateAsync(withdrawalRequest);
            return null;
        }, cancellationToken);

        if (refusal is not null)
        {
            logger.LogInformation(
                "User {UserId} asked to withdraw {Points} points but can withdraw {Withdrawable} (balance {Balance}, {PendingEarnings} on hold)",
                currentUser.Id, request.PointsRequested, refusal.Withdrawable, refusal.Balance, refusal.PendingEarnings);
            return new OperationResult
            {
                Success = false,
                Code = WithdrawalMessages.NotWithdrawableCode,
                Message = WithdrawalMessages.NotWithdrawable(refusal)
            };
        }

        logger.LogInformation(
            "User {UserId} requested withdrawal: {Points} points, {NetAmount} EGP via {Method}",
            currentUser.Id, request.PointsRequested, netAmount, request.WithdrawalMethod
        );

        return new OperationResult
        {
            Success = true,
            Message = $"أُرسل طلب السحب. ستستلم {RequestMessages.Egp(netAmount)} جنيه بعد خصم ضريبة قدرها {RequestMessages.Egp(tax)} جنيه. تتم مراجعة الطلب خلال 12 إلى 24 ساعة."
        };
    }
}
