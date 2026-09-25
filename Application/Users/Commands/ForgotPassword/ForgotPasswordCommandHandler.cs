using Application.Services;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.ForgotPassword;

public class ForgotPasswordCommandHandler(ILogger<ForgotPasswordCommandHandler> logger, UserManager<User> userManager, IEmailSender emailSender) : IRequestHandler<ForgotPasswordCommand, OperationResult>
{
    public async Task<OperationResult> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Password reset requested");
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            // Same answer as for a registered email, so the endpoint can't be used to find out who has an account.
            logger.LogInformation("Password reset requested for an email with no account");
            return Sent;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetPasswordLink = $"https://www.sardnovels.com/change-password?UserId={user.Id}&token={Uri.EscapeDataString(token)}";

        //sending email logic
        var templateId = "reset-password";
        var templateData = new { resetPasswordLink };
        await emailSender.SendTemplateEmailAsync(
            user.Email!,
            templateId,
            templateData
        );
        logger.LogInformation("Password reset email sent to user {UserId}", user.Id);
        return Sent;
    }

    private static OperationResult Sent => new()
    {
        Success = true,
        Message = "If an account uses this email, a password reset link has been sent to it."
    };
}
