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

    /// <summary>
    /// Whether the account has a password (false: it signs in with Google only). Says how to confirm deleting the
    /// account: with the password, or with Google.
    /// </summary>
    public bool HasPassword { get; set; }

    /// <summary>Who may browse the member's review list (#61): "Everyone" or "OnlyMe" (PATCH /api/User/me/privacy).</summary>
    public string ReviewsVisibility { get; set; } = default!;

    /// <summary>Who may browse the member's comment list (#61): "Everyone" or "OnlyMe" (PATCH /api/User/me/privacy).</summary>
    public string CommentsVisibility { get; set; } = default!;
}
