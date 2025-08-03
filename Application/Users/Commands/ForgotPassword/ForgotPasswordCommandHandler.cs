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
        logger.LogInformation("Generating reset token for email {email}", request.Email);
        var user = await userManager.FindByEmailAsync(request.Email) ?? throw new NotFoundException("No user with this email was found");
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetPasswordLink = $"http://localhost:5173/change-password?UserId={user.Id}&token={Uri.EscapeDataString(token)}";

        //sending email logic
        var templateId = "d-c5e9c7ae5e484b7ba0142380b5dda499";
        var templateData = new { resetPasswordLink };
        await emailSender.SendTemplateEmailAsync(
            user.Email!,
            templateId,
            templateData
        );
        return new OperationResult
        {
            Success = true,
            Message = "Reset password token was sent please check your mail!"
        };
    }
}
