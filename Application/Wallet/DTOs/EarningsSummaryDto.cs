namespace Application.Wallet.DTOs;

/// <summary>
/// GET /api/wallet/earnings (#78): what the signed-in member's novels earned her, in points only (no money, nothing
/// about payouts). Earnings are gifts (GiftReceived) and privilege subscriptions (PrivilegeRevenue) received; reversed is
/// what refunds took back of them (her side of EarningReversed).
/// </summary>
public class EarningsSummaryDto
{
    /// <summary>All her earnings less what was taken back of them; the same figure as GET /api/wallet.</summary>
    public decimal TotalEarned { get; set; }

    /// <summary>Earnings still on hold; the same figure as GET /api/wallet.</summary>
    public decimal PendingEarnings { get; set; }

    /// <summary>When the next of those is released (UTC); null when none is on hold. The same as GET /api/wallet.</summary>
    public DateTime? NextReleaseAt { get; set; }

    /// <summary>
    /// Every novel she has earnings from, of all time, by total (highest first; equal totals by novelId, the entry without
    /// a novel after the novels); its totals add up to <see cref="TotalEarned"/>. At most one entry has a null novelId:
    /// the earnings whose ledger rows don't say their novel (from before #17).
    /// </summary>
    public List<NovelEarningsDto> ByNovel { get; set; } = new();

    /// <summary>The last 12 calendar months in UTC, oldest first and the current one last, months without earnings included.</summary>
    public List<MonthEarningsDto> ByMonth { get; set; } = new();
}

public class NovelEarningsDto
{
    /// <summary>Null for the one entry of earnings whose novel isn't known.</summary>
    public Guid? NovelId { get; set; }

    /// <summary>The novel's slug, title and cover as they are now, for drafts and deleted novels too; null slug and cover for the entry without a novel.</summary>
    public string? NovelSlug { get; set; }

    /// <summary>«أرباح بلا رواية محددة» for the entry without a novel.</summary>
    public string? NovelTitle { get; set; }

    public string? CoverImageUrl { get; set; }

    /// <summary>Points earned from gifts to the novel.</summary>
    public decimal Gifts { get; set; }

    /// <summary>Points earned from privilege subscriptions to the novel.</summary>
    public decimal Privileges { get; set; }

    /// <summary>Points refunds took back of those, as a positive number.</summary>
    public decimal Reversed { get; set; }

    /// <summary>Gifts + Privileges − Reversed.</summary>
    public decimal Total { get; set; }

    /// <summary>
    /// How many members paid for these earnings, each once, accounts deleted since included: found through the payment
    /// each gift or subscription is linked to (from #22 on). Earnings from before have no link and count no one, so the
    /// entry without a novel has 0.
    /// </summary>
    public int SupportersCount { get; set; }
}

public class MonthEarningsDto
{
    /// <summary>The month, "yyyy-MM" (UTC).</summary>
    public string Month { get; set; } = default!;

    /// <summary>Points earned from gifts received that month.</summary>
    public decimal Gifts { get; set; }

    /// <summary>Points earned from privilege subscriptions that month.</summary>
    public decimal Privileges { get; set; }

    /// <summary>Points refunds took back that month (of earnings of that month or before), as a positive number.</summary>
    public decimal Reversed { get; set; }

    /// <summary>Gifts + Privileges − Reversed: below zero in a month that took back more than it earned.</summary>
    public decimal Total { get; set; }
}
