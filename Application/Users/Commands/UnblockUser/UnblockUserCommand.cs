using Application.Users.Commands.BlockUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.UnblockUser;

/// <summary>DELETE /api/User/unblock (or /api/User/block/{userId}): lifts the signed-in user's block. Idempotent.</summary>
public class UnblockUserCommand : IRequest<BlockUserResult>
{
    public string? UserId { get; set; }
}

public class UnblockUserCommandValidator : AbstractValidator<UnblockUserCommand>
{
    public UnblockUserCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty().WithMessage("حدد المستخدم");
    }
}

public class UnblockUserCommandHandler(
    ILogger<UnblockUserCommandHandler> logger,
    IUserContext userContext,
    UserManager<User> userManager,
    IUserBlocksRepository blocksRepository) : IRequestHandler<UnblockUserCommand, BlockUserResult>
{
    public async Task<BlockUserResult> Handle(UnblockUserCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var target = await BlockTargets.FindAsync(userManager, currentUser, request.UserId);

        if (await blocksRepository.UnblockAsync(currentUser.Id, target.Id, cancellationToken))
        {
            logger.LogInformation("User {UserId} unblocked {BlockedId}", currentUser.Id, target.Id);
        }

        // Follows the block removed stay removed.
        return new BlockUserResult { Success = true, Message = "تم إلغاء حظر المستخدم", UserId = target.Id, IsBlocked = false };
    }
}
