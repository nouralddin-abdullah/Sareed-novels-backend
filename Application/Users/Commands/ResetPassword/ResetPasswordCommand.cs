using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Application.Users.Commands;

public class ResetPasswordCommand : IRequest<IdentityResult>
{
    public string UserId { get; set; } = default!;
    public string Token { get; set; } = default!;
    public string NewPassword { get; set; } = default!;
}
