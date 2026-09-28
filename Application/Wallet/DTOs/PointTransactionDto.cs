namespace Application.Wallet.DTOs;

public class PointTransactionDto
{
    public Guid Id { get; set; }
    /// <summary>
    /// Machine-readable kind: RechargeApproved, WithdrawalApproved, GiftSent, GiftReceived, PrivilegeSubscription,
    /// PrivilegeRevenue, PlayPurchase, PlayRefund, BalanceForfeited or EarningReversed (entries from the wallet's first
    /// week may say Recharge or Withdrawal). EarningReversed (#22): negative, an author's earning taken back because the
    /// purchase that paid for it was refunded; positive, those points given back to the buyer whose refund took them.
    /// </summary>
    public string Type { get; set; } = default!;
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    /// <summary>Arabic, for people (entries from before #17 whose English couldn't be read with certainty stay English).</summary>
    public string Description { get; set; } = default!;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The novel of a gift or privilege entry, with its current slug and title (both null once it is deleted). Null for
    /// other types and for entries written before #17.
    /// </summary>
    public Guid? NovelId { get; set; }
    public string? NovelSlug { get; set; }
    public string? NovelTitle { get; set; }

    /// <summary>The gift of a GiftSent/GiftReceived entry and how many; null otherwise and before #17.</summary>
    public Guid? GiftId { get; set; }
    public int? GiftCount { get; set; }
}
