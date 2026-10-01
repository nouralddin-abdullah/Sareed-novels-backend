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
    /// <summary>
    /// The total of GET /api/User/{userName}/reviews as anyone signed out sees it (#54): the member's reviews on novels
    /// readers can open.
    /// </summary>
    public int ReviewsCount { get; set; }
    /// <summary>
    /// The total of GET /api/User/{userName}/comments as anyone signed out sees it (#54): the member's comments and
    /// replies on chapters and paragraphs readers can open. Comments on posts aren't counted (they were before #54).
    /// </summary>
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
    /// <summary>Whether the signed-in viewer blocked this user (so the app can offer to unblock); false when anonymous.</summary>
    public bool IsBlockedByMe { get; set; }

    /// <summary>
    /// Whether the member hid their review list from this viewer (#61): GET {userName}/reviews refuses it with 403
    /// ListHidden. Never for the member themselves. <see cref="ReviewsCount"/> still shows the real count.
    /// </summary>
    public bool ReviewsHidden { get; set; }

    /// <summary>
    /// Whether the member hid their comment list from this viewer (#61): GET {userName}/comments refuses it with 403
    /// ListHidden. Never for the member themselves. <see cref="CommentsCount"/> still shows the real count.
    /// </summary>
    public bool CommentsHidden { get; set; }
}
