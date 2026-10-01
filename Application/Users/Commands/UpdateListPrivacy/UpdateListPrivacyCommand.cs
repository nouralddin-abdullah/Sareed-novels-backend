using Application.Users.DTOS;
using MediatR;

namespace Application.Users.Commands.UpdateListPrivacy;

/// <summary>
/// PATCH /api/User/me/privacy: sets who may browse the caller's review and comment lists (#61). Each value is
/// "Everyone" or "OnlyMe" in any letter case (<see cref="UpdateListPrivacyCommandValidator"/>); one left out, or null,
/// keeps its setting, so an empty body changes nothing. Answers the settings, both of them.
/// </summary>
public class UpdateListPrivacyCommand : IRequest<ListPrivacyDto>
{
    public string? Reviews { get; set; }
    public string? Comments { get; set; }
}
