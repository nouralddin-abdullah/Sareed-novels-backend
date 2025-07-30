using Domain.Entities;
using Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.ResetPassword;

public class ResetPasswordCommandHandler(ILogger<ResetPasswordCommandHandler> logger, UserManager<User> userManager) : IRequestHandler<ResetPasswordCommand, IdentityResult>
{
    public async Task<IdentityResult> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Resetting password for user id: {userid}", request.UserId);
        var user = await userManager.FindByIdAsync(request.UserId) ?? throw new NotFoundException("The user was not found!");
        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        return result;
    }
}
