using Domain.Entities;

namespace Application.Users.DTOS;

public class UserIsProfile
{
    public string Id { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string UserBio { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? ProfilePhoto { get; set; }
    public string? ProfileBanner { get; set; }
    
    // Counters
    public int ReviewsCount { get; set; }
    public int CommentsCount { get; set; }
    public int LibraryNovelsCount { get; set; }
    
    // Social media links
    public string? FacebookUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? DiscordUrl { get; set; }
    public List<FollowerDto> RecentFollowing { get; set; } = new List<FollowerDto>();
    public int TotalFollowing { get; set; }
    public List<FollowerDto> RecentFollowers { get; set; } = new List<FollowerDto>();
    public int TotalFollowers { get; set; }
    public int RemainingFollowers => Math.Max(0, TotalFollowers - RecentFollowers.Count);
    public int RemainingFollowing => Math.Max(0, TotalFollowing - RecentFollowing.Count);

}
