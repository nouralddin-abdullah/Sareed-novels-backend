using Microsoft.AspNetCore.Identity;

namespace Domain.Entities;

public class User : IdentityUser
{
    public string DisplayName { get; set; } = default!;
    /// <summary>Normalized display name + user name for search; maintained by ApplicationDbContext on save.</summary>
    public string SearchName { get; set; } = string.Empty;
    public string? ProfilePhoto { get; set; }
    public string? ProfileBanner { get; set; }
    public string? UserBio { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Counters
    public int ReviewsCount { get; set; } = 0;
    public int CommentsCount { get; set; } = 0;
    public int LibraryNovelsCount { get; set; } = 0;
    
    // Wallet (cached for fast profile display)
    public decimal PointBalance { get; set; } = 0;
    public DateTime PointBalanceLastUpdated { get; set; } = DateTime.UtcNow;
    
    // Social media links
    public string? FacebookUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? DiscordUrl { get; set; }

    /// <summary>
    /// Access tokens issued before this moment (UTC, whole seconds) are refused; null when none were ever revoked.
    /// Set through ITokenRevocationService (sign out everywhere).
    /// </summary>
    public DateTime? TokensValidAfter { get; set; }

    /// <summary>
    /// A moderator's suspension (UTC): until then the account can't sign in and its access tokens are refused.
    /// <see cref="Moderation.Suspension.Permanent"/> for good; null when not suspended. Set through IAccountSuspensionService.
    /// </summary>
    public DateTime? SuspendedUntil { get; set; }

    public ICollection<Follow> Following { get; set; } = new List<Follow>();
    public ICollection<Follow> Followers { get; set; } = new List<Follow>();
    public ICollection<Novel> Novels { get; set; } = new List<Novel>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<ReviewLike> ReviewLikes { get; set; } = new List<ReviewLike>();
    public ICollection<CommentLikes> CommentLikes { get; set; } = new List<CommentLikes>();
    public ICollection<Comments> Comments { get; set; } = new List<Comments>();
    public ICollection<ReadingList> ReadingLists { get; set; } = new List<ReadingList>();
    public ICollection<ReadingListFollower> FollowedReadingLists { get; set; } = new List<ReadingListFollower>();
    
    // Helper methods for counters
    public void IncrementLibraryNovelsCount() => LibraryNovelsCount++;
    public void DecrementLibraryNovelsCount() => LibraryNovelsCount = Math.Max(0, LibraryNovelsCount - 1);

    /// <summary>
    /// The <see cref="TokensValidAfter"/> value that refuses tokens issued before <paramref name="utcNow"/>. Tokens
    /// record their issue time in whole seconds, so the cut-off is too: a token issued later in the same second
    /// (the fresh one for the session that revoked) stays valid.
    /// </summary>
    public static DateTime TokenCutoff(DateTime utcNow) =>
        new(utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
