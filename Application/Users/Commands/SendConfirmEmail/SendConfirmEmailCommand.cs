using MediatR;

namespace Application.Users.Commands.SendConfirmEmail;

public class SendConfirmEmailCommand : IRequest
{
    public string Email { get; set; } = default!;
}
