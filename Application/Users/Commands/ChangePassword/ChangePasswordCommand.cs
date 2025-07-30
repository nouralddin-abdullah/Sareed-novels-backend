using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Application.Users.Commands.ChangePassword;

public class ChangePasswordCommand : IRequest<IdentityResult>
{
    public string CurrentPassword { get; set; } = default!;
    public string NewPassword { get; set; } = default!;
}
