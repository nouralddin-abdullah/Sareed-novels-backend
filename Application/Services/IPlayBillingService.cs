using Application.Wallet.DTOs;

namespace Application.Services;

/// <summary>
/// Point packs sold in the Android app through Google Play Billing. The server verifies every purchase with the Google
/// Play Developer API, credits it once per purchase token, consumes it on Google Play itself (the app must not consume
/// or acknowledge), and takes the points back if Google voids the purchase.
/// </summary>
public interface IPlayBillingService
{
    /// <summary>The packs the app may offer (product id and points, never prices), or Enabled = false when purchases can't be verified.</summary>
    PlayProductsDto GetProducts(string userId);

    /// <summary>
    /// Verifies a purchase with Google Play and credits its points to <paramref name="userId"/> once per purchase token:
    /// a replay by the same user answers the same points again; the token is refused for anyone else.
    /// </summary>
    Task<PlayPurchaseResult> VerifyPurchaseAsync(string userId, string? productId, string? purchaseToken, string? orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes back the points of a purchase Google voided, even below zero, once per purchase token; a token nobody claimed
    /// yet is recorded so it can't be credited later. Returns true if it changed anything. Fed by the voided-purchases
    /// poll; a Real-time Developer Notifications endpoint can call it too.
    /// </summary>
    Task<bool> ApplyVoidAsync(PlayVoidedPurchase voided, CancellationToken cancellationToken = default);
}
