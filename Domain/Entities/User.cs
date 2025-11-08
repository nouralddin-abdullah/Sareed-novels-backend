using Microsoft.AspNetCore.Identity;

namespace Domain.Entities;

public class User : IdentityUser
{
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
    public string? ProfileBanner { get; set; }
    public string? UserBio { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Counters
    public int ReviewsCount { get; set; } = 0;
    public int CommentsCount { get; set; } = 0;
    public int LibraryNovelsCount { get; set; } = 0;
    
    // Social media links
    public string? FacebookUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? DiscordUrl { get; set; }

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
    public void IncrementReviewsCount() => ReviewsCount++;
    public void DecrementReviewsCount() => ReviewsCount = Math.Max(0, ReviewsCount - 1);
    public void IncrementCommentsCount() => CommentsCount++;
    public void DecrementCommentsCount() => CommentsCount = Math.Max(0, CommentsCount - 1);
    public void IncrementLibraryNovelsCount() => LibraryNovelsCount++;
    public void DecrementLibraryNovelsCount() => LibraryNovelsCount = Math.Max(0, LibraryNovelsCount - 1);
}
