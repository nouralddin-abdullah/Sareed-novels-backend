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
        logger.LogInformation("Generating email confirmation token for {email}:", request.Email);
        var user = await userManager.FindByEmailAsync(request.Email) ?? throw new NotFoundException("The user not found");
        var token = await usersRepository.GenerateEmailToken(user);
        var confirmationLink = $"https://sardnovels.com/confirm-email?UserId={user.Id}&token={Uri.EscapeDataString(token)}";

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
