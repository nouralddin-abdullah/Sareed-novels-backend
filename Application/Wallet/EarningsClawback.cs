using Domain.Repositories;

namespace Application.Wallet;

/// <summary>
/// The arithmetic of the refund clawback (#22 rule 4): how much of a refund the buyer's balance couldn't cover, and which
/// of the authors' earnings still on hold take it back.
/// </summary>
public static class EarningsClawback
{
    /// <summary>
    /// How far below zero the refund of <paramref name="refunded"/> points took the balance (it ended at
    /// <paramref name="balanceAfter"/>): 0 when the balance covered it, never more than the refund itself (a balance that
    /// was already below zero owes that from before).
    /// </summary>
    public static decimal Deficit(decimal refunded, decimal balanceAfter) => Math.Min(refunded, Math.Max(0, -balanceAfter));

    /// <summary>
    /// Takes <paramref name="deficit"/> from <paramref name="heldNewestFirst"/> in that order (the buyer's latest payment
    /// first), from each at most what is left of it, until it is covered; earnings with nothing left are skipped.
    /// </summary>
    public static IReadOnlyList<(HeldEarning Earning, decimal Amount)> Allocate(decimal deficit, IEnumerable<HeldEarning> heldNewestFirst)
    {
        var reversals = new List<(HeldEarning, decimal)>();
        var uncovered = deficit;
        foreach (var earning in heldNewestFirst)
        {
            if (uncovered <= 0)
            {
                break;
            }

            var amount = Math.Min(uncovered, earning.Remaining);
            if (amount <= 0)
            {
                continue;
            }

            reversals.Add((earning, amount));
            uncovered -= amount;
        }
        return reversals;
    }
}
