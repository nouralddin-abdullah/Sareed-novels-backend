using System.Text.Json.Serialization;
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

        // A refused sign-up (400) also has the shape of every other error (#25), next to result (which the web reads):
        // code and message the same as result's, and errors every problem Identity found ({code, description}), such as
        // both DuplicateUserName and DuplicateEmail. Left out of a successful answer.

        /// <summary>Identity's code for the first problem (DuplicateUserName, DuplicateEmail, InvalidUserName...).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Code { get; set; }

        /// <summary>For people, in Arabic: every problem, in one message.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<IdentityError>? Errors { get; set; }
    }
}
