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
        logger.LogInformation("Trying to login for {loginCardinality}", request.LoginCardinality);
        if (request.LoginCardinality.Contains('@'))
        {
            user = await userManager.FindByEmailAsync(request.LoginCardinality);
        }
        else
        {
            user = await userManager.FindByNameAsync(request.LoginCardinality);
        }

        if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
            throw new ForbidException("Invalid email or password");

        var accessToken = jWTService.GenerateAccessToken(user);
        var expiresAt = DateTime.UtcNow.AddDays(60);
        // store accessToken for the user
        return new UserLoginResult(accessToken, expiresAt);

    }
}
