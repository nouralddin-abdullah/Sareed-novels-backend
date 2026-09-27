using Application.Services;
using Application.Users;
using Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Reports.Commands.LiftSuspension;

/// <summary>DELETE /api/admin/users/{userId}/suspension: the user can sign in again. Idempotent.</summary>
public class LiftSuspensionCommand(string userId) : IRequest<LiftSuspensionResult>
{
    public string UserId { get; } = userId;
}

public record LiftSuspensionResult(string UserId, bool IsSuspended);

public class LiftSuspensionCommandHandler(
    ILogger<LiftSuspensionCommandHandler> logger,
    IUserContext userContext,
    IAccountSuspensionService suspensions) : IRequestHandler<LiftSuspensionCommand, LiftSuspensionResult>
{
    public async Task<LiftSuspensionResult> Handle(LiftSuspensionCommand request, CancellationToken cancellationToken)
    {
        if (!await suspensions.LiftAsync(request.UserId, cancellationToken))
        {
            throw new NotFoundException("المستخدم غير موجود", "UserNotFound");
        }

        logger.LogInformation("Admin {AdminId} lifted the suspension of user {UserId}", userContext.GetCurrentUser()?.Id, request.UserId);
        return new LiftSuspensionResult(request.UserId, IsSuspended: false);
    }
}
