using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Application.Users.Commands.ConfirmEmail;

public class ConfirmEmailCommand : IRequest<IdentityResult>
{
    public string UserId { get; set; } = default!;
    public string Token { get; set; } = default!;
}
