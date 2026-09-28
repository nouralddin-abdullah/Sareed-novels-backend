namespace Application.Wallet.DTOs;

/// <summary>
/// Why a Google Play purchase was not credited. The name is the stable <c>code</c> the API returns, so the app never
/// has to parse the Arabic message.
/// </summary>
public enum PlayPurchaseError
{
    /// <summary>productId or purchaseToken missing or malformed.</summary>
    InvalidRequest,
    /// <summary>The product id isn't a point pack Sard sells.</summary>
    UnknownProduct,
    /// <summary>The server isn't set up to verify purchases (or Google refuses its credentials). Retry later.</summary>
    BillingUnavailable,
    /// <summary>Google Play couldn't be reached or failed. Retry later; nothing was credited.</summary>
    VerificationUnavailable,
    /// <summary>Google Play doesn't know this token for this app and product.</summary>
    PurchaseNotFound,
    /// <summary>The purchase is for another product than the one sent (or than the one it was credited as).</summary>
    ProductMismatch,
    /// <summary>The payment isn't complete yet (Play purchaseState 2). Send it again once Play reports it purchased.</summary>
    PurchasePending,
    /// <summary>The purchase was canceled (Play purchaseState 1).</summary>
    PurchaseCanceled,
    /// <summary>Google voided the purchase (refund, chargeback, revocation).</summary>
    PurchaseVoided,
    /// <summary>A license-tester purchase while test purchases aren't accepted.</summary>
    TestPurchaseNotAllowed,
    /// <summary>More than one pack in one purchase (Play multi-quantity), which Sard doesn't sell.</summary>
    UnsupportedQuantity,
    /// <summary>The purchase's obfuscatedExternalAccountId isn't the caller's (or is missing).</summary>
    AccountMismatch,
    /// <summary>This purchase token was already credited to another user.</summary>
    AlreadyUsedByAnotherUser
}

public sealed class PlayPurchaseResult
{
    private PlayPurchaseResult(PlayPurchaseError? error, int pointsAdded, decimal currentBalance)
    {
        Error = error;
        PointsAdded = pointsAdded;
        CurrentBalance = currentBalance;
    }

    public PlayPurchaseError? Error { get; }
    public bool Success => Error is null;

    /// <summary>The points this purchase added; the same for every replay of the same purchase.</summary>
    public int PointsAdded { get; }

    /// <summary>The wallet balance after the purchase (after a replay: the balance now).</summary>
    public decimal CurrentBalance { get; }

    public string? Code => Error?.ToString();
    public string? Message => Error is { } error ? PlayPurchaseMessages.For(error) : null;

    public static PlayPurchaseResult Credited(int pointsAdded, decimal currentBalance) => new(null, pointsAdded, currentBalance);

    public static PlayPurchaseResult Failed(PlayPurchaseError error) => new(error, 0, 0);
}

/// <summary>The Arabic text the app shows for each <see cref="PlayPurchaseError"/>.</summary>
public static class PlayPurchaseMessages
{
    public const string Unavailable = "الشراء عبر Google Play غير متاح حاليًا. حاول لاحقًا.";

    public static string For(PlayPurchaseError error) => error switch
    {
        PlayPurchaseError.InvalidRequest => "بيانات عملية الشراء ناقصة أو غير صحيحة.",
        PlayPurchaseError.UnknownProduct => "هذه الباقة غير متوفرة للشراء.",
        PlayPurchaseError.BillingUnavailable => Unavailable,
        PlayPurchaseError.VerificationUnavailable => "تعذّر التحقق من عملية الشراء لدى Google Play الآن. أعد المحاولة بعد قليل.",
        PlayPurchaseError.PurchaseNotFound => "لم يتعرّف Google Play على عملية الشراء هذه.",
        PlayPurchaseError.ProductMismatch => "عملية الشراء لا تطابق الباقة المختارة.",
        PlayPurchaseError.PurchasePending => "لم يكتمل الدفع بعد. ستُضاف النقاط بعد أن يؤكد Google Play الدفع.",
        PlayPurchaseError.PurchaseCanceled => "أُلغيت عملية الشراء هذه، فلم تُضف أي نقاط.",
        PlayPurchaseError.PurchaseVoided => "أُلغيت عملية الشراء هذه أو استُرد مبلغها، فلا يمكن إضافة نقاطها.",
        PlayPurchaseError.TestPurchaseNotAllowed => "عمليات الشراء التجريبية غير مقبولة.",
        PlayPurchaseError.UnsupportedQuantity => "لا يمكن شراء أكثر من باقة واحدة في العملية نفسها. سيردّ Google Play المبلغ تلقائيًا خلال ثلاثة أيام.",
        PlayPurchaseError.AccountMismatch => "عملية الشراء هذه غير مرتبطة بحسابك في سرد.",
        PlayPurchaseError.AlreadyUsedByAnotherUser => "استُخدمت عملية الشراء هذه في حساب آخر.",
        _ => Unavailable
    };
}
