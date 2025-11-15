namespace Application.Users.DTOS;

public class UserProfile
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
    
    // Following/Followers totals only
    public int TotalFollowing { get; set; }
    public int TotalFollowers { get; set; }
    public bool IsFollowing { get; set; }
}
