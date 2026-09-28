namespace Domain.Entities;

public class PointTransaction
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    
    public string Type { get; set; } = default!; // Recharge, Withdrawal, UnlockChapter, Gift, etc.
    public decimal Amount { get; set; } // Positive for additions, negative for deductions
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    
    /// <summary>For people, in Arabic; clients can build their own text from <see cref="Type"/> and the ids below.</summary>
    public string Description { get; set; } = default!;
    /// <summary>
    /// The record the row is about: the RechargeRequest or WithdrawalRequest, the PlayPurchase (also on its PlayRefund
    /// and on the EarningReversed rows it caused), or, on both rows of a gift or privilege subscription, the
    /// GiftTransaction or NovelPrivilegeSubscription (which pairs a GiftSent with its GiftReceived; rows before #22 have none).
    /// </summary>
    public Guid? RelatedRequestId { get; set; }

    /// <summary>
    /// The novel a gift or privilege subscription was for (no foreign key: the ledger outlives the novel). Null for
    /// other types, and for rows written before #17.
    /// </summary>
    public Guid? NovelId { get; set; }

    /// <summary>The gift sent or received, and how many of it; null for other types and rows before #17.</summary>
    public Guid? GiftId { get; set; }
    public int? GiftCount { get; set; }

    /// <summary>
    /// Earning rows (<see cref="Constants.TransactionType.Earnings"/>): when the points become withdrawable (UTC),
    /// CreatedAt plus the hold (Wallet:EarningsHoldDays, #22). Earnings written before #22 were released at once
    /// (AvailableAt = CreatedAt). Null on every other type; the database refuses an earning row without it.
    /// </summary>
    public DateTime? AvailableAt { get; set; }

    /// <summary>
    /// EarningReversed rows: the earning row (GiftReceived, PrivilegeRevenue) whose points were taken back, on the
    /// author's row and on the buyer's. No foreign key, like NovelId. Null on every other type.
    /// </summary>
    public Guid? ReversedTransactionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
