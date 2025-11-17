using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Wallet.Commands.RequestRecharge;

public class RequestRechargeCommandHandler(
    ILogger<RequestRechargeCommandHandler> logger,
    IUserContext userContext,
    IRechargeRequestRepository rechargeRepository,
    IPointCalculationService calculationService,
    IFileUploadService fileUploadService) : IRequestHandler<RequestRechargeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RequestRechargeCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

        // Validate minimum points
        if (request.PointsRequested < PointsConstants.MinimumRecharge)
        {
            return new OperationResult
            {
                Success = false,
                Message = $"Minimum recharge is {PointsConstants.MinimumRecharge} points"
            };
        }

        // Validate payment method
        if (request.PaymentMethod != Domain.Constants.PaymentMethod.VodafoneCash &&
            request.PaymentMethod != Domain.Constants.PaymentMethod.InstaPay &&
            request.PaymentMethod != Domain.Constants.PaymentMethod.PayPal)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Invalid payment method. Use VodafoneCash, InstaPay, or PayPal"
            };
        }

        // Validate payment proof
        if (request.PaymentProof == null || request.PaymentProof.Length == 0)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Payment proof is required"
            };
        }

        // Validate file size (5MB max)
        if (request.PaymentProof.Length > 5 * 1024 * 1024)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Payment proof must be less than 5MB"
            };
        }

        // Validate file type
        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "application/pdf" };
        if (!allowedTypes.Contains(request.PaymentProof.ContentType.ToLower()))
        {
            return new OperationResult
            {
                Success = false,
                Message = "Payment proof must be JPG, PNG, or PDF"
            };
        }

        // Calculate amounts
        var (basePrice, fee, total) = calculationService.CalculateRechargeTotal(request.PointsRequested);

        // Upload payment proof
        string paymentProofUrl;
        try
        {
            using var stream = request.PaymentProof.OpenReadStream();
            paymentProofUrl = await fileUploadService.UploadPaymentProofAsync(
                stream,
                request.PaymentProof.ContentType,
                currentUser.Id
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload payment proof for user {UserId}", currentUser.Id);
            return new OperationResult
            {
                Success = false,
                Message = "Failed to upload payment proof. Please try again."
            };
        }

        // Create recharge request
        var rechargeRequest = new RechargeRequest
        {
            Id = Guid.NewGuid(),
            UserId = currentUser.Id,
            PointsRequested = request.PointsRequested,
            BaseAmountEGP = basePrice,
            TransactionFee = fee,
            TotalAmountEGP = total,
            PaymentMethod = request.PaymentMethod,
            PaymentProofUrl = paymentProofUrl,
            Status = RequestStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };

        await rechargeRepository.CreateAsync(rechargeRequest);

        logger.LogInformation(
            "User {UserId} requested recharge: {Points} points, {Total} EGP via {Method}",
            currentUser.Id, request.PointsRequested, total, request.PaymentMethod
        );

        return new OperationResult
        {
            Success = true,
            Message = $"Recharge request submitted successfully. Total: {total} EGP. Please wait 12-24 hours for approval."
        };
    }
}
