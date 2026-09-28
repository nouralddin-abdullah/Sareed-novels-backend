using Domain.Constants;

namespace Domain.Entities;

/// <summary>
/// A Google Play purchase of a point pack: one row per purchase token (unique index), which is what stops a token from
/// being credited twice, to the same user or to another one. <see cref="Status"/> says what Sard did with it.
/// A row can also be a marker for a purchase Google voided before anyone claimed it (<see cref="PlayPurchaseStatus.VoidedUnclaimed"/>,
/// no user and no product): it only stops that token from being credited later.
/// </summary>
public class PlayPurchase
{
    /// <summary>Google documents purchase tokens as up to 150 characters; this leaves room.</summary>
    public const int PurchaseTokenMaxLength = 512;
    public const int ProductIdMaxLength = 150;
    public const int OrderIdMaxLength = 100;
    public const int ConsumeErrorMaxLength = 500;

    public Guid Id { get; set; }
    public string PurchaseToken { get; set; } = default!;

    /// <summary>The user the points went to; null for a <see cref="PlayPurchaseStatus.VoidedUnclaimed"/> marker.</summary>
    public string? UserId { get; set; }
    public User? User { get; set; }

    public string? ProductId { get; set; }

    /// <summary>Google's order id (GPA.xxxx-xxxx-xxxx-xxxxx), for support; license-test purchases may have none.</summary>
    public string? OrderId { get; set; }

    /// <summary>Points credited for this purchase, and taken back if Google voids it. 0 when never credited.</summary>
    public int Points { get; set; }

    public string Status { get; set; } = PlayPurchaseStatus.Credited;

    /// <summary>A license-tester purchase (Play's purchaseType 0), credited only while test purchases are allowed.</summary>
    public bool IsTestPurchase { get; set; }

    /// <summary>When the user bought it, as Google reports it.</summary>
    public DateTime? PurchasedAt { get; set; }

    /// <summary>When Sard recorded it.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When Sard consumed it on Google Play, which also acknowledges it and lets the user buy the pack again.</summary>
    public DateTime? ConsumedAt { get; set; }

    /// <summary>When the background worker should next try to consume it; null when nothing is owed (consumed or voided).</summary>
    public DateTime? NextConsumeAttemptAt { get; set; }

    public int ConsumeAttempts { get; set; }
    public string? LastConsumeError { get; set; }

    /// <summary>When Google voided it (refund, chargeback, revocation), as Google reports it; null if Google gave no time.</summary>
    public DateTime? VoidedAt { get; set; }

    /// <summary>Google's voidedReason (0 other, 1 remorse, 2 not received, 3 defective, 4 accidental, 5 fraud, 6 friendly fraud, 7 chargeback).</summary>
    public int? VoidedReason { get; set; }

    /// <summary>Google's voidedSource (0 user, 1 developer, 2 Google).</summary>
    public int? VoidedSource { get; set; }
}
