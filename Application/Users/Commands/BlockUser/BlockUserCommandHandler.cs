using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.BlockUser;

public class BlockUserCommandHandler(
    ILogger<BlockUserCommandHandler> logger,
    IUserContext userContext,
    UserManager<User> userManager,
    IUserBlocksRepository blocksRepository) : IRequestHandler<BlockUserCommand, BlockUserResult>
{
    public async Task<BlockUserResult> Handle(BlockUserCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var target = await BlockTargets.FindAsync(userManager, currentUser, request.UserId);

        // Also removes any follow between the two and what the blocked user caused this one to be notified of.
        if (await blocksRepository.BlockAsync(currentUser.Id, target.Id, cancellationToken))
        {
            logger.LogInformation("User {UserId} blocked {BlockedId}", currentUser.Id, target.Id);
        }

        return new BlockUserResult { Success = true, Message = "تم حظر المستخدم", UserId = target.Id, IsBlocked = true };
    }
}

internal static class BlockTargets
{
    /// <summary>The user to block or unblock: never oneself (400), and one that exists (404).</summary>
    public static async Task<User> FindAsync(UserManager<User> userManager, CurrentUser currentUser, string? userId)
    {
        var id = userId?.Trim() ?? "";
        if (string.Equals(id, currentUser.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("لا يمكنك حظر نفسك", "CannotBlockSelf");
        }

        return await userManager.FindByIdAsync(id)
            ?? throw new NotFoundException("المستخدم غير موجود", "UserNotFound");
    }
}
