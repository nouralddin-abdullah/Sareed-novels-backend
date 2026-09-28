namespace Domain.Constants;

public static class TransactionType
{
    // Earning types
    public const string RechargeApproved = "RechargeApproved";
    public const string GiftReceived = "GiftReceived";
    public const string PrivilegeRevenue = "PrivilegeRevenue"; // ✅ NEW: Author receives privilege subscription

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
}
