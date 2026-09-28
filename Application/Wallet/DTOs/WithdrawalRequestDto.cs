using System.Text.Json.Serialization;

namespace Application.Wallet.DTOs;

public class WithdrawalRequestDto
{
    public Guid Id { get; set; }
    public int PointsRequested { get; set; }
    public decimal BaseAmountEGP { get; set; }
    public decimal TaxDeducted { get; set; }
    public decimal NetAmountEGP { get; set; }
    public string WithdrawalMethod { get; set; } = default!;
    public string PaymentDetails { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? RejectionReason { get; set; }

    /// <summary>
    /// The member cancelled it themselves (#27, DELETE /api/wallet/withdraw/{id}): its status is Rejected, with the
    /// rejection reason «ألغاه صاحب الطلب», so apps can show it as cancelled rather than refused.
    /// </summary>
    public bool CancelledByOwner { get; set; }

    // For admin view
    public string? UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public string? UserEmail { get; set; }

    /// <summary>
    /// Admin list only (#22): what approving can pay the requester now, their released earnings less what was withdrawn
    /// or reversed, never more than their balance. Their pending requests, this one included, aren't taken out: a
    /// request is approved only when its points are at most this, and each approval lowers it for the next.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? RequesterWithdrawable { get; set; }

    /// <summary>
    /// Admin list only (#22): the requester's EarningReversed rows of the last 90 days, newest first, at most 10. A
    /// negative one took back earnings of theirs because the purchase that paid for them was refunded; a positive one
    /// gave them back points after a refund of their own purchase. Empty when there are none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<EarningReversalDto>? RecentEarningReversals { get; set; }
}

/// <summary>An EarningReversed ledger row, for the admin's payout review (#22).</summary>
public class EarningReversalDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Arabic, as the member's wallet shows it.</summary>
    public string Description { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    /// <summary>The refunded Google Play purchase (PlayPurchases.Id).</summary>
    public Guid? PurchaseId { get; set; }
    /// <summary>The earning row (GiftReceived, PrivilegeRevenue) that was taken back.</summary>
    public Guid? ReversedTransactionId { get; set; }
    public Guid? NovelId { get; set; }
}
