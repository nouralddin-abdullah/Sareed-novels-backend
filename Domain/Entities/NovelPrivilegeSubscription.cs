namespace Domain.Entities;

/// <summary>
/// Represents a user's subscription to a novel's privilege system.
/// Subscribers can read all privilege-locked chapters.
/// NOTE: Subscriptions are PERMANENT - they never expire!
/// </summary>
public class NovelPrivilegeSubscription
{
    public Guid Id { get; set; }
    
    // Relationships
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;
    
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    
    // Subscription Details
    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true; // Can be manually cancelled by user
    
    // Payment Information
    public decimal AmountPaid { get; set; } // Points spent for subscription
    public Guid? PaymentTransactionId { get; set; } // Reference to PointTransaction
    
    // Metadata
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; } // "UserCancelled", "RefundIssued", etc.
    
    /// <summary>
    /// Checks if subscription is currently valid and active
    /// Since subscriptions are permanent, only checks IsActive flag
    /// </summary>
    public bool IsValidSubscription()
    {
        return IsActive;
    }
}
