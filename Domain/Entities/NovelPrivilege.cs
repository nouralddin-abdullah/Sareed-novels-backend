namespace Domain.Entities;

/// <summary>
/// Represents the privilege (premium chapter lock) configuration for a novel.
/// Authors can enable this to lock the latest N chapters behind a subscription paywall.
/// </summary>
public class NovelPrivilege
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;
    
    // Core Settings
    public bool IsEnabled { get; set; } = false;
    public int MaxLockedChapters { get; set; } = 20; // Always lock max 20 chapters
    
    // Pricing
    public decimal SubscriptionCost { get; set; } = 100; // Points required to subscribe (100-2000)
    // NOTE: Subscriptions are PERMANENT - no expiration date
    
    // Current State
    public int CurrentLockedCount { get; set; } = 0; // How many chapters are currently locked (0-20)
    public int? PrivilegeStartSequence { get; set; } // Which PublishedChapterSequence privilege starts from
    
    // Daily Unlock Tracking
    public DateTime? LastDailyUnlockDate { get; set; } // Last time daily unlock was triggered (UTC)
    public int TotalDailyUnlocksPerformed { get; set; } = 0; // Total count of daily unlocks since creation
    
    // Requirements
    public int MinPublishedRequired { get; set; } = 11; // Need at least 11 published chapters (first 10 must be free)
    
    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation
    public ICollection<NovelPrivilegeSubscription> Subscriptions { get; set; } = new List<NovelPrivilegeSubscription>();
    
    /// <summary>
    /// Validates if the subscription cost is within allowed range (100-2000 points)
    /// </summary>
    public bool IsValidSubscriptionCost()
    {
        return SubscriptionCost >= 100 && SubscriptionCost <= 2000;
    }
    
    /// <summary>
    /// Checks if privilege can be enabled (minimum published chapters requirement)
    /// </summary>
    public bool CanBeEnabled(int currentPublishedCount)
    {
        return currentPublishedCount >= MinPublishedRequired;
    }
}
