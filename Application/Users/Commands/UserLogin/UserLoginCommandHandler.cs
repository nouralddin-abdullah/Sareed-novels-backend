using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.UserLogin;

public class UserLoginCommandHandler(UserManager<User> userManager, ILogger<UserLoginCommandHandler> logger, IJWTService jWTService) : IRequestHandler<UserLoginCommand, UserLoginResult>
{
    public async Task<UserLoginResult> Handle(UserLoginCommand request, CancellationToken cancellationToken)
    {
        User? user;
        logger.LogInformation("Sign-in attempt");
        if (request.LoginCardinality.Contains('@'))
        {
            user = await userManager.FindByEmailAsync(request.LoginCardinality);
        }
        else
        {
            user = await userManager.FindByNameAsync(request.LoginCardinality);
        }

        if (user == null)
            throw new ForbidException("Invalid email or password");

        // Identity lockout: after MaxFailedAccessAttempts wrong passwords the account refuses sign-in for a while.
        if (await userManager.IsLockedOutAsync(user))
            throw new TooManyRequestsException("Too many failed sign-in attempts. Try again in a few minutes.");

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            throw new ForbidException("Invalid email or password");
        }

        if (await userManager.GetAccessFailedCountAsync(user) > 0)
            await userManager.ResetAccessFailedCountAsync(user);

        var accessToken = jWTService.GenerateAccessToken(user);
        var expiresAt = DateTime.UtcNow.AddDays(60);
        // store accessToken for the user
        return new UserLoginResult(accessToken, expiresAt);

    }
}
