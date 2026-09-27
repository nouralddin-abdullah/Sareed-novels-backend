using System.ComponentModel.DataAnnotations;
using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Users.Commands.UpdateMe;

/// <summary>
/// A field left out (null) stays as it is. A field sent empty is bound as "" (not turned into null, as model binding
/// otherwise does): it clears the bio and the links, and the validator rejects it for the user name and display name.
/// </summary>
public class UpdateMeCommand : IRequest<OperationResult>
{
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? UserName { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? DisplayName { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? UserBio { get; set; }
    public IFormFile? ProfilePhoto { get; set; }
    public IFormFile? ProfileBanner { get; set; }
    
    // Social media links
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? FacebookUrl { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? TwitterUrl { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? DiscordUrl { get; set; }
}
