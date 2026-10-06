namespace Domain.Constants;

public static class TransactionType
{
    // Bought points: spent inside Sard only, never withdrawable (#22)
    public const string RechargeApproved = "RechargeApproved"; // A website top-up an admin approved

    // Earnings: what an author earns from readers, the only points that can be withdrawn, once held long enough (#22)
    public const string GiftReceived = "GiftReceived";
    public const string PrivilegeRevenue = "PrivilegeRevenue"; // Author receives privilege subscription

    // Spending types
    public const string WithdrawalApproved = "WithdrawalApproved";
    public const string GiftSent = "GiftSent";
    public const string PrivilegeSubscription = "PrivilegeSubscription"; // Reader subscribes to novel privilege
    
    // Refunds
    public const string Refund = "Refund"; // General refund

    // Google Play point packs
    public const string PlayPurchase = "PlayPurchase"; // Points bought in the Android app through Google Play Billing
    public const string PlayRefund = "PlayRefund"; // Google voided a Play purchase (refund, chargeback): its points taken back, even below zero

    // Account deletion
    public const string BalanceForfeited = "BalanceForfeited"; // The member deleted their account: the balance went to zero (from above or below)

    // Refund clawback (#22): an earning still on hold, taken back because the purchase that paid for it was refunded.
    // Two rows per reversal: negative on the author (the balance may go below zero), positive on the buyer, whose
    // refund already took those points; both point at the voided purchase and at the reversed earning row.
    public const string EarningReversed = "EarningReversed";

    // The wallet's first week wrote these names for what is now RechargeApproved and WithdrawalApproved, and its rows
    // still carry them. Never written now.
    public const string Recharge = "Recharge";
    public const string Withdrawal = "Withdrawal";

    /// <summary>
    /// Every type above, the first week's names included: the names the types filter of GET /api/wallet/transactions
    /// accepts (#92). A unit test fails when a type is added above and not here.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        RechargeApproved, GiftReceived, PrivilegeRevenue, WithdrawalApproved, GiftSent, PrivilegeSubscription, Refund,
        PlayPurchase, PlayRefund, BalanceForfeited, EarningReversed, Recharge, Withdrawal
    ];

    /// <summary>The earning types: only these become withdrawable, after the hold.</summary>
    public static readonly IReadOnlyList<string> Earnings = [GiftReceived, PrivilegeRevenue];

    public static bool IsEarning(string type) => type is GiftReceived or PrivilegeRevenue;
}
