using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Wallet.Commands.RejectWithdrawal;

public class RejectWithdrawalCommandHandler(
    ILogger<RejectWithdrawalCommandHandler> logger,
    IUserContext userContext,
    IWithdrawalRequestRepository withdrawalRepository) : IRequestHandler<RejectWithdrawalCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RejectWithdrawalCommand request, CancellationToken cancellationToken)
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

        if (string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return new OperationResult
            {
                Success = false,
                Message = "Rejection reason is required"
            };
        }

        // Update request status
        withdrawalRequest.Status = RequestStatus.Rejected;
        withdrawalRequest.ProcessedAt = DateTime.UtcNow;
        withdrawalRequest.ProcessedBy = currentUser.Id;
        withdrawalRequest.RejectionReason = request.RejectionReason;

        await withdrawalRepository.UpdateAsync(withdrawalRequest);

        logger.LogInformation(
            "Admin {AdminId} rejected withdrawal {RequestId} for user {UserId}. Reason: {Reason}",
            currentUser.Id, request.RequestId, withdrawalRequest.UserId, request.RejectionReason
        );

        return new OperationResult
        {
            Success = true,
            Message = "Withdrawal request rejected successfully"
        };
    }
}
