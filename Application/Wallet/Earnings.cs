using Domain.Constants;
using Domain.Repositories;

namespace Application.Wallet;

/// <summary>What a ledger row adds to an author's earnings (#78).</summary>
public enum EarningKind
{
    /// <summary>GiftReceived: a reader's gift to one of her novels.</summary>
    Gift,

    /// <summary>PrivilegeRevenue: a reader's privilege (early access) subscription to one of her novels.</summary>
    Privilege,

    /// <summary>Her side of EarningReversed (negative): an earning a refund took back.</summary>
    Reversed
}

/// <summary>
/// An author's earnings (#78), read from her ledger: what readers' gifts (GiftReceived) and privilege subscriptions
/// (PrivilegeRevenue) earned her, less what refunds took back of them (her side of EarningReversed, the negative one).
/// The other side of a reversal, positive, is on whom paid: their own payment given back to them, never an earning, so it
/// doesn't count even when that member is an author too. The one rule for GET /api/wallet's totalEarned and for the
/// earnings summary (GET /api/wallet/earnings).
/// </summary>
public static class Earnings
{
    /// <summary>How the row counts towards earnings; null when it doesn't (every other type, and a reversal's positive side).</summary>
    public static EarningKind? KindOf(string type, decimal amount) => type switch
    {
        TransactionType.GiftReceived => EarningKind.Gift,
        TransactionType.PrivilegeRevenue => EarningKind.Privilege,
        TransactionType.EarningReversed when amount < 0 => EarningKind.Reversed,
        _ => null
    };

    /// <summary>
    /// What the user has earned in all: the signed sum of her earning rows, so earnings less what was taken back of them.
    /// Never below zero, since a reversal takes back at most what is left of its earning.
    /// </summary>
    public static decimal Net(IEnumerable<LedgerEntry> ledger) =>
        ledger.Where(row => KindOf(row.Type, row.Amount) is not null).Sum(row => row.Amount);
}
