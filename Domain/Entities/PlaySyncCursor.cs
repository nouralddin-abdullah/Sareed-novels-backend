namespace Domain.Entities;

/// <summary>
/// How far a background sync with Google Play has got, persisted so it survives app-pool recycles. Today there is
/// one: the voided-purchases poll ("VoidedPurchases").
/// </summary>
public class PlaySyncCursor
{
    public string Name { get; set; } = default!;

    /// <summary>Everything Google recorded before this time has been processed.</summary>
    public DateTime SyncedUntil { get; set; }

    public DateTime UpdatedAt { get; set; }
}
