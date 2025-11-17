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
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

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

        if (string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return new OperationResult
            {
                Success = false,
                Message = "Rejection reason is required"
            };
        }

        // Update request status
        rechargeRequest.Status = RequestStatus.Rejected;
        rechargeRequest.ProcessedAt = DateTime.UtcNow;
        rechargeRequest.ProcessedBy = currentUser.Id;
        rechargeRequest.RejectionReason = request.RejectionReason;

        await rechargeRepository.UpdateAsync(rechargeRequest);

        logger.LogInformation(
            "Admin {AdminId} rejected recharge {RequestId} for user {UserId}. Reason: {Reason}",
            currentUser.Id, request.RequestId, rechargeRequest.UserId, request.RejectionReason
        );

        return new OperationResult
        {
            Success = true,
            Message = "Recharge request rejected successfully"
        };
    }
}
