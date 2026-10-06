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
/// GET /api/novel/{novelId}/privilege when early access is on (#94): its settings, its locked chapters now, and the
/// signed-in member's subscription. Off, the answer is only <c>{ isEnabled: false }</c>.
/// </summary>
public class PrivilegeInfoDto
{
    public bool IsEnabled { get; set; }
    public decimal SubscriptionCost { get; set; }

    /// <summary>How many days a chapter stays locked from when its lock started (1-30); null when <see cref="SubscribersOnly"/>.</summary>
    public int? EarlyAccessDays { get; set; }

    /// <summary>Locked chapters stay locked for non-subscribers until the author frees them.</summary>
    public bool SubscribersOnly { get; set; }

    /// <summary>How many published chapters are locked for non-subscribers now, counted from the chapters.</summary>
    public int LockedChaptersCount { get; set; }

    /// <summary>The earliest moment a locked chapter opens to everyone (UTC, "Z"); null when none is locked, or subscribers only.</summary>
    public DateTime? NextUnlockAt { get; set; }

    /// <summary>
    /// The published position of the first locked chapter; the number after the last published chapter when none is
    /// locked (what the website reads as "locking starts from").
    /// </summary>
    public int? PrivilegeStartSequence { get; set; }
    public int TotalPublishedChapters { get; set; }

    /// <summary>How many members subscribed: for the novel's author only, null for anyone else.</summary>
    public int? SubscribersCount { get; set; }

    public bool IsSubscribed { get; set; } // Does current user have subscription?

    /// <summary>The limits of the settings (#96), so the app needn't hard-code them; also in the answer while it is off.</summary>
    public EarlyAccessRulesDto Rules { get; set; } = EarlyAccessRulesDto.Current;
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

/// <summary>A subscriber of the novel's early access, for its author (#96: GET .../privilege/subscribers).</summary>
public class PrivilegeSubscriberDto
{
    public string UserId { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }

    /// <summary>When they subscribed, UTC with "Z".</summary>
    public DateTime SubscribedAt { get; set; }
}

/// <summary>
/// The limits of early access's settings (#96), from <see cref="EarlyAccess"/>: the subscription's price in whole points,
/// the first chapters that always stay free, the most chapters enabling locks, and the days a lock can last (and the
/// default when none is given).
/// </summary>
public class EarlyAccessRulesDto
{
    public static readonly EarlyAccessRulesDto Current = new();

    public decimal MinCost => EarlyAccess.MinCost;
    public decimal MaxCost => EarlyAccess.MaxCost;
    public int FreeChapters => EarlyAccess.FreeChapters;
    public int MaxLockedOnEnable => EarlyAccess.MaxLockedWhenEnabled;
    public int MinDays => EarlyAccess.MinDays;
    public int MaxDays => EarlyAccess.MaxDays;
    public int DefaultDays => EarlyAccess.DefaultDays;
}
