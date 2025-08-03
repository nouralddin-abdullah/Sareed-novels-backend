using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Application.Users.Commands.CreateUser
{
    public class CreateUserCommand : IRequest<CreateUserResponse>
    {
        public string UserName { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string Password { get; set; } = default!;
        public string DisplayName { get; set; } = default!;
        public IFormFile? ProfilePhoto { get; set; }

    }

    public class CreateUserResponse
    {
        public OperationResult Result { get; set; } = default!;
        public string? AccessToken { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
