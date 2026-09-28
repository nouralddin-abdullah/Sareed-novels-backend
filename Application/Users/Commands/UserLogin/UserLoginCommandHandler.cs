using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Moderation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.UserLogin;

public class UserLoginCommandHandler(UserManager<User> userManager, ILogger<UserLoginCommandHandler> logger, IJWTService jWTService, TimeProvider time) : IRequestHandler<UserLoginCommand, UserLoginResult>
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
            throw new ForbidException("البريد أو اسم المستخدم أو كلمة المرور غير صحيحة", "InvalidCredentials");

        // Identity lockout: after MaxFailedAccessAttempts wrong passwords the account refuses sign-in for a while.
        if (await userManager.IsLockedOutAsync(user))
            throw new TooManyRequestsException("توقف تسجيل الدخول مؤقتًا بعد محاولات خاطئة متكررة. حاول بعد 5 دقائق، أو أعد تعيين كلمة المرور.", "TooManySignInAttempts");

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            throw new ForbidException("البريد أو اسم المستخدم أو كلمة المرور غير صحيحة", "InvalidCredentials");
        }

        if (await userManager.GetAccessFailedCountAsync(user) > 0)
            await userManager.ResetAccessFailedCountAsync(user);

        // A moderator suspended the account. Checked after the password, so only its owner learns of it.
        if (Suspension.IsActive(user.SuspendedUntil, time.GetUtcNow().UtcDateTime))
        {
            logger.LogInformation("Sign-in refused: user {UserId} is suspended", user.Id);
            throw new AccountSuspendedException(user.SuspendedUntil!.Value);
        }

        var accessToken = jWTService.GenerateAccessToken(user);
        var expiresAt = DateTime.UtcNow.AddDays(60);
        // store accessToken for the user
        return new UserLoginResult(accessToken, expiresAt);

    }
}
