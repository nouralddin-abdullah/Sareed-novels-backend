namespace Application.Privileges.DTOs;

/// <summary>
/// Privilege configuration details for a novel
/// </summary>
public class NovelPrivilegeDto
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    public bool IsEnabled { get; set; }
    public decimal SubscriptionCost { get; set; }
    public int MaxLockedChapters { get; set; }
    public int CurrentLockedCount { get; set; }
    public int? PrivilegeStartSequence { get; set; }
    public int MinPublishedRequired { get; set; }
    public DateTime? LastDailyUnlockDate { get; set; }
    public int TotalDailyUnlocksPerformed { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Privilege info for readers (includes subscription status)
/// </summary>
public class PrivilegeInfoDto
{
    public bool IsEnabled { get; set; }
    public decimal SubscriptionCost { get; set; }
    public int LockedChaptersCount { get; set; }
    public int? PrivilegeStartSequence { get; set; } // NEW: Which sequence privilege starts from
    public int TotalPublishedChapters { get; set; }
    public bool IsSubscribed { get; set; } // Does current user have subscription?
    /// <summary>When the signed-in reader's subscription began (UTC); null when not subscribed.</summary>
    public DateTime? SubscribedAt { get; set; }
    /// <summary>
    /// Always false: a subscription is a permanent unlock and can't be cancelled (the owner's rule, #17); the cancel
    /// endpoint answers 400 SubscriptionCannotBeCancelled.
    /// </summary>
    public bool CanCancel => false;
}

/// <summary>
/// User's privilege subscription
/// </summary>
public class PrivilegeSubscriptionDto
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    /// <summary>The novel's current slug (subscriptions to deleted novels aren't listed).</summary>
    public string? NovelSlug { get; set; }
    public string NovelTitle { get; set; } = default!;
    public string? NovelCoverImageUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime SubscribedAt { get; set; }
    public decimal AmountPaid { get; set; }
}

/// <summary>
/// Novel subscriber info (for author view)
/// </summary>
public class PrivilegeSubscriberDto
{
    public string UserId { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
    public DateTime SubscribedAt { get; set; }
    public decimal AmountPaid { get; set; }
}
