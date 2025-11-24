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
}
