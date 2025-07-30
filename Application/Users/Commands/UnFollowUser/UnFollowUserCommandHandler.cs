using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.UnFollowUser;

public class UnFollowUserCommandHandler(ILogger<UnFollowUserCommandHandler> logger, IUserContext userContext, UserManager<User> userManager, IUsersRepository usersRepository) : IRequestHandler<UnFollowUserCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UnFollowUserCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("The user is not authorized");
        logger.LogInformation("User {UserId} trying to unfollow {UserId}", currentUser.Id, request.UserToUnFollowId);
        var userToUnFollow = await userManager.FindByIdAsync(request.UserToUnFollowId) ?? throw new NotFoundException("User you trying to unfollow not found");

        if (currentUser.Id == userToUnFollow.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You cannot unfollow yourself",
            };
        }
        var isFollowing = await usersRepository.IsFollowingAsync(currentUser.Id, userToUnFollow.Id);

        if (!isFollowing)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You already not following this user",
            };
        }

        var result = await usersRepository.UnFollowUser(currentUser.Id, userToUnFollow.Id);
        string message = result ? $"You are now not following {userToUnFollow.DisplayName}" : "Failed to unfollow the user";
        return new OperationResult
        {
            Success = result,
            Message = message
        };
    }
}
