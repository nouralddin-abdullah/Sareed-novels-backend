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
    
    public string Description { get; set; } = default!;
    public Guid? RelatedRequestId { get; set; } // Links to RechargeRequest/WithdrawalRequest
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
