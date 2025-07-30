using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.ConfirmEmail;

public class ConfirmEmailCommandHandler(ILogger<ConfirmEmailCommandHandler> logger, UserManager<User> userManager, IUsersRepository usersRepository) : IRequestHandler<ConfirmEmailCommand, IdentityResult>
{
    public async Task<IdentityResult> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Trying to confirm email for {userid}", request.UserId);
        var user = await userManager.FindByIdAsync(request.UserId) ?? throw new NotFoundException("The user trying to verify email for it not found.");
        return await usersRepository.ConfirmEmail(user, request.Token);

    }
}
