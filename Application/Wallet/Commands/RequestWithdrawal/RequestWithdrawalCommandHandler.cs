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
    IWalletService walletService) : IRequestHandler<RequestWithdrawalCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RequestWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

        // Validate minimum points
        if (request.PointsRequested < PointsConstants.MinimumWithdrawal)
        {
            return new OperationResult
            {
                Success = false,
                Message = $"Minimum withdrawal is {PointsConstants.MinimumWithdrawal} points"
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
                Message = "Invalid withdrawal method. Use VodafoneCash, InstaPay, or PayPal"
            };
        }

        // Validate payment details
        if (string.IsNullOrWhiteSpace(request.PaymentDetails))
        {
            return new OperationResult
            {
                Success = false,
                Message = "Payment details are required (phone number or email)"
            };
        }

        // Check sufficient balance
        if (!await walletService.HasSufficientBalanceAsync(currentUser.Id, request.PointsRequested))
        {
            return new OperationResult
            {
                Success = false,
                Message = $"Insufficient balance. You need at least {request.PointsRequested} points."
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

        await withdrawalRepository.CreateAsync(withdrawalRequest);

        logger.LogInformation(
            "User {UserId} requested withdrawal: {Points} points, {NetAmount} EGP via {Method}",
            currentUser.Id, request.PointsRequested, netAmount, request.WithdrawalMethod
        );

        return new OperationResult
        {
            Success = true,
            Message = $"Withdrawal request submitted successfully. You will receive {netAmount} EGP (after {tax} EGP tax). Please wait 12-24 hours for processing."
        };
    }
}
