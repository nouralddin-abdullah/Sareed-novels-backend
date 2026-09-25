using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.SendConfirmEmail;

public class SendConfirmEmailCommandHandler(UserManager<User> userManager, ILogger<SendConfirmEmailCommandHandler> logger, IUsersRepository usersRepository, IEmailSender emailSender) : IRequestHandler<SendConfirmEmailCommand>
{
    public async Task Handle(SendConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null || user.EmailConfirmed)
        {
            // Answer the same either way, so the endpoint can't be used to find out who has an account.
            logger.LogInformation("Confirmation email not sent: no account or already confirmed");
            return;
        }

        logger.LogInformation("Generating email confirmation token for user {UserId}", user.Id);
        var token = await usersRepository.GenerateEmailToken(user);
        var confirmationLink = $"https://www.sardnovels.com/confirm-email?UserId={user.Id}&token={Uri.EscapeDataString(token)}";

        //sending email logic
        var templateId = "confirm-email";
        var templateData = new { confirmationLink };
        await emailSender.SendTemplateEmailAsync(
            user.Email!,
            templateId,
            templateData
        );
    }
}
