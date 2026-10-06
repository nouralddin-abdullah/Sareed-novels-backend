namespace Domain.Entities;

/// <summary>
/// A novel's early access («الوصول المبكر», #94): whether it is on, what a subscription costs, and how long a chapter
/// stays early. Which chapters are locked is stored on the chapters (<see cref="Chapter.EarlyAccessFrom"/>,
/// <see cref="Chapter.EarlyAccessFreedAt"/>), and who they lock out is the rule of <c>EarlyAccess</c>. One row per novel,
/// kept when early access is turned off, so its subscriptions stay.
/// </summary>
public class NovelPrivilege
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;
    
    /// <summary>On: new chapters lock as they come out, and locked chapters are open to subscribers only.</summary>
    public bool IsEnabled { get; set; } = false;
    
    /// <summary>Points a subscription costs (100-2000); a change applies to new subscribers. Subscriptions are permanent.</summary>
    public decimal SubscriptionCost { get; set; } = 100;

    /// <summary>
    /// How many days a chapter stays locked from when its lock started (#94): 1 to 30, 7 unless set. Null when
    /// <see cref="SubscribersOnly"/>; never both (a check constraint).
    /// </summary>
    public int? EarlyAccessDays { get; set; } = 7;

    /// <summary>Locked chapters stay locked for non-subscribers until the author frees them (#94): no automatic unlock.</summary>
    public bool SubscribersOnly { get; set; }

    // The positional window that came before #94: a start position among the published chapters, a stored count of the
    // chapters from there, moved by a daily job. Nothing reads or writes them any more; the columns stay for one release
    // so that the version before can still run against this database, and go in a later migration.
    public int MaxLockedChapters { get; set; } = 20;
    public int CurrentLockedCount { get; set; } = 0;
    public int? PrivilegeStartSequence { get; set; }
    public DateTime? LastDailyUnlockDate { get; set; }
    public int TotalDailyUnlocksPerformed { get; set; } = 0;
    public int MinPublishedRequired { get; set; } = 11;
    
    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation
    public ICollection<NovelPrivilegeSubscription> Subscriptions { get; set; } = new List<NovelPrivilegeSubscription>();
}
