namespace Application.Wallet.DTOs;

/// <summary>
/// A purchase Google Play reports voided: an entry of the Voided Purchases API today, and what a Real-time Developer
/// Notifications (voidedPurchaseNotification) handler would pass in later.
/// </summary>
public sealed record PlayVoidedPurchase(
    string PurchaseToken,
    string? OrderId,
    DateTime? VoidedAt,
    int? VoidedReason,
    int? VoidedSource);
