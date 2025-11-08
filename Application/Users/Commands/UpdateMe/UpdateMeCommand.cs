using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Users.Commands.UpdateMe;

public class UpdateMeCommand : IRequest<OperationResult>
{
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public string? UserBio { get; set; }
    public IFormFile? ProfilePhoto { get; set; }
    public IFormFile? ProfileBanner { get; set; }
    
    // Social media links
    public string? FacebookUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? DiscordUrl { get; set; }
}
