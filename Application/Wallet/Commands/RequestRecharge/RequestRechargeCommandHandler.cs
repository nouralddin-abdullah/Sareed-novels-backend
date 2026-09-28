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
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        // Validate minimum points
        if (request.PointsRequested < PointsConstants.MinimumRecharge)
        {
            return new OperationResult
            {
                Success = false,
                Code = "BelowMinimumRecharge",
                Message = $"الحد الأدنى للشحن {PointsConstants.MinimumRecharge} نقطة"
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
                Code = "InvalidPaymentMethod",
                Message = "طريقة الدفع غير صالحة. اختر فودافون كاش أو إنستاباي أو باي بال"
            };
        }

        // Validate payment proof
        if (request.PaymentProof == null || request.PaymentProof.Length == 0)
        {
            return new OperationResult
            {
                Success = false,
                Code = "PaymentProofRequired",
                Message = "أرفق إثبات الدفع"
            };
        }

        // Validate file size (5MB max)
        if (request.PaymentProof.Length > 5 * 1024 * 1024)
        {
            return new OperationResult
            {
                Success = false,
                Code = "PaymentProofTooLarge",
                Message = "يجب ألا يتجاوز حجم إثبات الدفع 5 ميغابايت"
            };
        }

        // Validate file type
        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "application/pdf" };
        if (!allowedTypes.Contains(request.PaymentProof.ContentType.ToLower()))
        {
            return new OperationResult
            {
                Success = false,
                Code = "InvalidPaymentProofType",
                Message = "إثبات الدفع يجب أن يكون صورة JPG أو PNG أو ملف PDF"
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
                Code = "UploadFailed",
                Message = "تعذّر رفع إثبات الدفع. حاول مرة أخرى."
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
            Message = $"أُرسل طلب الشحن، والمبلغ الإجمالي {RequestMessages.Egp(total)} جنيه. تتم مراجعة الطلب خلال 12 إلى 24 ساعة."
        };
    }
}
