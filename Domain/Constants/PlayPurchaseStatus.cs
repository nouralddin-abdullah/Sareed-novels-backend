namespace Domain.Constants;

/// <summary>What Sard did with a Google Play purchase (<see cref="Domain.Entities.PlayPurchase.Status"/>).</summary>
public static class PlayPurchaseStatus
{
    /// <summary>Verified with Google Play and its points credited.</summary>
    public const string Credited = "Credited";

    /// <summary>Credited, then voided by Google (refund, chargeback, revocation): its points were taken back.</summary>
    public const string Voided = "Voided";

    /// <summary>Voided by Google before anyone claimed it: never credited, and never creditable.</summary>
    public const string VoidedUnclaimed = "VoidedUnclaimed";
}
