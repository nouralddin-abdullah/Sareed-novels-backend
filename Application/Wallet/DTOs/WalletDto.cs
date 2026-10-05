namespace Application.Wallet.DTOs;

public class WalletDto
{
    public decimal CurrentBalance { get; set; }
    public decimal TotalRecharged { get; set; }
    public decimal TotalWithdrawn { get; set; }
    public decimal TotalSpent { get; set; }

    /// <summary>
    /// What the user's earnings (gifts and privilege subscriptions received) came to in all, less what refunds took back of
    /// them (#78); the same figure as GET /api/wallet/earnings. It was always 0 before: the column it was read from was
    /// never written.
    /// </summary>
    public decimal TotalEarned { get; set; }

    /// <summary>
    /// What can be requested as a withdrawal now (#22): earnings (gifts and privilege subscriptions received) whose hold
    /// has ended, less withdrawals paid or pending, and never more than the balance. Bought points never count.
    /// </summary>
    public decimal Withdrawable { get; set; }

    /// <summary>Earnings still on hold: withdrawable once the hold ends (Wallet:EarningsHoldDays after each was received).</summary>
    public decimal PendingEarnings { get; set; }

    /// <summary>When the next of the pending earnings becomes withdrawable (UTC); null when none is on hold.</summary>
    public DateTime? NextReleaseAt { get; set; }
}
