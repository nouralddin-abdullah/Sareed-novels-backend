using Domain.Repositories;

namespace Application.Wallet;

/// <summary>
/// The refund clawback (#22 rule 4, #27): how much of a refund the buyer's balance couldn't cover, and which earnings
/// still on hold take it back. A refund that takes the buyer below zero means they gave away points they never paid for:
/// every earning they paid for that is still on hold can be taken back, newest payment first. When taking one back takes
/// its author below zero in turn, the author passed those points on, and their own payments are taken back the same way,
/// at most <see cref="MaxCascadeDepth"/> accounts further down.
/// </summary>
public static class EarningsClawback
{
    /// <summary>How many accounts past the buyer's own payments the clawback follows the points.</summary>
    public const int MaxCascadeDepth = 3;

    /// <summary>
    /// How far below zero taking <paramref name="taken"/> points took a balance that ended at
    /// <paramref name="balanceAfter"/>: 0 when the balance covered it, never more than <paramref name="taken"/> (a balance
    /// that was below zero already owes that from before).
    /// </summary>
    public static decimal Deficit(decimal taken, decimal balanceAfter) => Math.Min(taken, Math.Max(0, -balanceAfter));

    /// <summary>
    /// Takes <paramref name="deficit"/> from <paramref name="heldNewestFirst"/> in that order (the latest payment first),
    /// from each at most what is left of it, until it is covered; earnings with nothing left are skipped.
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

    /// <summary><paramref name="Amount"/> of <paramref name="Earning"/> taken back from its author and given to
    /// <paramref name="PayerId"/>, who paid for it; <paramref name="Depth"/> 0 is the buyer's own payments.</summary>
    public sealed record Reversal(HeldEarning Earning, string PayerId, decimal Amount, int Depth);

    /// <summary>What a refund does.</summary>
    /// <param name="Deficit">How far below zero the refund took the buyer.</param>
    /// <param name="Reversals">The earnings taken back, in the order they are written.</param>
    /// <param name="Wallets">Every wallet it changes, the buyer's first.</param>
    /// <param name="BalancesAfter">Each of those wallets' balance once it is done.</param>
    /// <param name="Missing">Wallets it needs but may not read (not locked): the plan stopped there and is incomplete.</param>
    public sealed record Plan(
        decimal Deficit,
        IReadOnlyList<Reversal> Reversals,
        IReadOnlyList<string> Wallets,
        IReadOnlyDictionary<string, decimal> BalancesAfter,
        IReadOnlyList<string> Missing)
    {
        /// <summary>What was taken back from the buyer's own payments, and given back to them.</summary>
        public decimal ReturnedToBuyer => Reversals.Where(r => r.Depth == 0).Sum(r => r.Amount);

        /// <summary>What no earning on hold could cover: the buyer's loss.</summary>
        public decimal Uncovered => Deficit - ReturnedToBuyer;
    }

    /// <summary>
    /// Works out the refund of <paramref name="refunded"/> points from <paramref name="buyerId"/> and its clawback.
    /// The buyer's payments are walked first; each author whom a reversal takes below zero has their own payments walked
    /// next (breadth first), for how far below zero that took them, up to <see cref="MaxCascadeDepth"/> accounts past the
    /// buyer's payments. Each account's payments are walked once, which ends cycles; an account walked already can still
    /// have an earning taken back (a gift back to the buyer is taken back from the buyer, whose loss it was).
    /// </summary>
    /// <param name="balanceOf">A wallet's balance, or null when this plan may not read it (a wallet not locked).</param>
    /// <param name="heldPaidBy">The earnings still on hold a user paid for, newest payment first, with what is left of each.</param>
    public static async Task<Plan> PlanAsync(string buyerId, decimal refunded, Func<string, Task<decimal?>> balanceOf,
        Func<string, Task<IReadOnlyList<HeldEarning>>> heldPaidBy)
    {
        var balances = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var buyerBalance = await balanceOf(buyerId) ?? throw new InvalidOperationException($"The buyer's wallet ({buyerId}) must be readable");
        balances[buyerId] = buyerBalance - refunded;

        var deficit = Deficit(refunded, balances[buyerId]);
        var reversals = new List<Reversal>();
        var wallets = new List<string> { buyerId };
        var missing = new List<string>();
        var walked = new HashSet<string>(StringComparer.Ordinal) { buyerId };
        var queue = new Queue<(string Payer, decimal Owed, int Depth)>();
        queue.Enqueue((buyerId, deficit, 0));

        while (queue.TryDequeue(out var step))
        {
            if (step.Owed <= 0)
            {
                continue;
            }

            var allocation = Allocate(step.Owed, await heldPaidBy(step.Payer));
            var authors = allocation.Select(a => a.Earning.AuthorId).Distinct(StringComparer.Ordinal).ToList();
            foreach (var author in authors.Where(a => !balances.ContainsKey(a)))
            {
                if (await balanceOf(author) is { } balance)
                {
                    balances[author] = balance;
                }
                else
                {
                    missing.Add(author);
                }
            }
            if (missing.Count > 0)
            {
                break; // decided with a balance it can't trust: the caller locks these too and starts again
            }

            var takenFrom = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var (earning, amount) in allocation)
            {
                balances[earning.AuthorId] -= amount;
                balances[step.Payer] += amount;
                reversals.Add(new Reversal(earning, step.Payer, amount, step.Depth));
                takenFrom[earning.AuthorId] = takenFrom.GetValueOrDefault(earning.AuthorId) + amount;
                if (!wallets.Contains(earning.AuthorId, StringComparer.Ordinal))
                {
                    wallets.Add(earning.AuthorId);
                }
            }

            if (step.Depth == MaxCascadeDepth)
            {
                continue;
            }
            foreach (var author in authors)
            {
                var owed = Deficit(takenFrom[author], balances[author]);
                if (owed > 0 && walked.Add(author))
                {
                    queue.Enqueue((author, owed, step.Depth + 1));
                }
            }
        }

        return new Plan(deficit, reversals, wallets, balances, missing);
    }
}
