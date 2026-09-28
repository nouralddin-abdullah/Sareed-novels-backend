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
    public Guid? RelatedRequestId { get; set; } // Links to RechargeRequest/WithdrawalRequest

    /// <summary>
    /// The novel a gift or privilege subscription was for (no foreign key: the ledger outlives the novel). Null for
    /// other types, and for rows written before #17.
    /// </summary>
    public Guid? NovelId { get; set; }

    /// <summary>The gift sent or received, and how many of it; null for other types and rows before #17.</summary>
    public Guid? GiftId { get; set; }
    public int? GiftCount { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
