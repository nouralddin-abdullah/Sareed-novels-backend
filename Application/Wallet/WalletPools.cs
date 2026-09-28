using Domain.Constants;
using Domain.Repositories;

namespace Application.Wallet;

/// <summary>An earning still on hold: when it is released, and what is left of it.</summary>
public sealed record HeldLot(Guid EarningId, DateTime AvailableAt, decimal Remaining);

/// <summary>
/// A user's points by where they came from (#27), worked out from their ledger read in time order. The ledger is the
/// only record: the same rows give the same pools to the wallet page, a withdrawal request, its approval and a refund.
/// <list type="bullet">
/// <item><b>Bought</b>: website top-ups (RechargeApproved), Google Play packs (PlayPurchase), what a refund's clawback gave
/// back to its payer (the positive EarningReversed row) and a balance from before the ledger. Never withdrawable.</item>
/// <item><b>Held</b>: one lot per earning (GiftReceived, PrivilegeRevenue) until its AvailableAt.</item>
/// <item><b>Released</b>: earnings whose hold has ended, the only points a withdrawal pays.</item>
/// <item><b>Deficit</b>: what a refund took beyond everything the user had (a negative balance). New credits pay it off
/// before they count anywhere else, so the pools never hold points while the user owes some.</item>
/// </list>
/// Spending (gifts, privilege subscriptions) comes out of Bought, then Held (the lot released last first, so older
/// earnings keep their release date), then Released. So does every other debit: a Play refund, a forfeited balance. The
/// author's side of a reversal takes back that very earning while it is held, and whatever of it was spent already like
/// any other debit. An approved withdrawal comes out of Released; one approved before earnings were held (#22) may be
/// larger than that, and the rest comes out of Bought, then Held, so it isn't counted against later earnings.
/// Bought + Held + Released − Deficit is the balance at every step.
/// </summary>
public sealed class WalletPools
{
    private WalletPools(decimal bought, IReadOnlyList<HeldLot> held, decimal released, decimal deficit)
    {
        Bought = bought;
        Held = held;
        Released = released;
        Deficit = deficit;
    }

    public decimal Bought { get; }

    /// <summary>The lots still on hold with something left, soonest release first.</summary>
    public IReadOnlyList<HeldLot> Held { get; }

    public decimal Released { get; }

    public decimal Deficit { get; }

    /// <summary>Earnings still on hold: withdrawable once released, unless spent or taken back first.</summary>
    public decimal PendingEarnings => Held.Sum(l => l.Remaining);

    /// <summary>When the next held lot is released (UTC); null when none is on hold.</summary>
    public DateTime? NextReleaseAt => Held.Count == 0 ? null : DateTime.SpecifyKind(Held[0].AvailableAt, DateTimeKind.Utc);

    /// <summary>What the pools add up to: the wallet balance.</summary>
    public decimal Balance => Bought + PendingEarnings + Released - Deficit;

    /// <summary>
    /// The pools of a wallet whose balance is <paramref name="balance"/> and whose ledger is <paramref name="ledger"/>, at
    /// <paramref name="asOf"/>. What the balance holds beyond the ledger's sum is from before the ledger: Bought when
    /// positive, a deficit when negative, placed before the first row.
    /// </summary>
    public static WalletPools Fold(decimal balance, IEnumerable<LedgerEntry> ledger, DateTime asOf)
    {
        var rows = ledger as IReadOnlyCollection<LedgerEntry> ?? ledger.ToList();
        var fold = new Folding();
        var opening = balance - rows.Sum(r => r.Amount);
        fold.Credit(opening > 0 ? opening : 0);
        fold.Owe(opening < 0 ? -opening : 0);

        foreach (var row in InOrder(rows, opening))
        {
            // Rows written after asOf (a clock ahead of this one) count as written at asOf: no lot is released early.
            var at = row.CreatedAt < asOf ? row.CreatedAt : asOf;
            fold.ReleaseUpTo(at);
            fold.Apply(row, at);
        }
        fold.ReleaseUpTo(asOf);
        return fold.Result();
    }

    /// <summary>
    /// The rows by CreatedAt. Rows of one moment (tests on a fixed clock; in production every row gets its own) follow the
    /// balances they recorded, each starting where the one before ended; failing that, credits come first.
    /// </summary>
    private static IEnumerable<LedgerEntry> InOrder(IReadOnlyCollection<LedgerEntry> rows, decimal opening)
    {
        var balance = opening;
        foreach (var moment in rows.GroupBy(r => r.CreatedAt).OrderBy(g => g.Key))
        {
            var left = moment.OrderByDescending(r => r.Amount).ThenBy(r => r.Id).ToList();
            while (left.Count > 0)
            {
                var next = left.Find(r => r.BalanceBefore == balance) ?? left[0];
                left.Remove(next);
                balance = next.BalanceAfter;
                yield return next;
            }
        }
    }

    private sealed class Lot(Guid earningId, DateTime availableAt, decimal remaining)
    {
        public Guid EarningId { get; } = earningId;
        public DateTime AvailableAt { get; } = availableAt;
        public decimal Remaining { get; set; } = remaining;
    }

    private enum Pool { Bought, Held, Released }

    private static readonly Pool[] SpendingOrder = [Pool.Bought, Pool.Held, Pool.Released];
    private static readonly Pool[] WithdrawalOrder = [Pool.Released, Pool.Bought, Pool.Held];

    private sealed class Folding
    {
        /// <summary>
        /// The held lots from <see cref="head"/> on, by release (then by when they were earned): the next one released
        /// first, the one released last at the end. Those before <see cref="head"/> are released.
        /// </summary>
        private readonly List<Lot> lots = new();
        private readonly Dictionary<Guid, Lot> held = new();
        private int head;
        private decimal bought;
        private decimal released;
        private decimal deficit;

        public void Owe(decimal amount) => deficit += amount;

        /// <summary>A credit that isn't an earning: it pays the deficit first, the rest is Bought.</summary>
        public void Credit(decimal amount) => bought += PayDeficit(amount);

        public void ReleaseUpTo(DateTime at)
        {
            for (; head < lots.Count && lots[head].AvailableAt <= at; head++)
            {
                released += lots[head].Remaining;
                held.Remove(lots[head].EarningId);
            }
        }

        private void Hold(Lot lot)
        {
            // Earned later, released later, as long as the hold doesn't shrink: then it goes before those released later.
            var at = lots.Count;
            while (at > head && lots[at - 1].AvailableAt > lot.AvailableAt)
            {
                at--;
            }
            lots.Insert(at, lot);
            held[lot.EarningId] = lot;
        }

        public void Apply(LedgerEntry row, DateTime at)
        {
            if (row.Amount > 0)
            {
                if (!TransactionType.IsEarning(row.Type))
                {
                    Credit(row.Amount);
                    return;
                }

                var left = PayDeficit(row.Amount);
                if (left == 0)
                {
                    return;
                }

                switch (row.AvailableAt)
                {
                    case { } availableAt when availableAt > at:
                        Hold(new Lot(row.Id, availableAt, left));
                        break;
                    case not null:
                        released += left; // released at once: an earning from before the hold (AvailableAt = CreatedAt)
                        break;
                    default:
                        bought += left; // an earning without a release date (the database refuses one): never withdrawable
                        break;
                }
                return;
            }

            if (row.Amount == 0)
            {
                return;
            }

            var amount = -row.Amount;
            switch (row.Type)
            {
                case TransactionType.EarningReversed:
                    // The author's side: that very earning, as far as it is still held and unspent.
                    if (row.ReversedTransactionId is { } earningId && held.TryGetValue(earningId, out var lot))
                    {
                        var taken = Math.Min(amount, lot.Remaining);
                        lot.Remaining -= taken;
                        amount -= taken;
                    }
                    Debit(amount, SpendingOrder);
                    break;
                case TransactionType.WithdrawalApproved:
                    Debit(amount, WithdrawalOrder);
                    break;
                default:
                    Debit(amount, SpendingOrder);
                    break;
            }
        }

        private decimal PayDeficit(decimal amount)
        {
            var paid = Math.Min(amount, deficit);
            deficit -= paid;
            return amount - paid;
        }

        private void Debit(decimal amount, Pool[] order)
        {
            foreach (var pool in order)
            {
                if (amount <= 0)
                {
                    return;
                }
                amount -= Take(pool, amount);
            }
            deficit += amount;
        }

        private decimal Take(Pool pool, decimal amount)
        {
            switch (pool)
            {
                case Pool.Bought:
                {
                    var taken = Math.Min(amount, bought);
                    bought -= taken;
                    return taken;
                }
                case Pool.Released:
                {
                    var taken = Math.Min(amount, released);
                    released -= taken;
                    return taken;
                }
                default:
                {
                    // The lot released last first; lots used up at the end are dropped, so the next debit starts at one
                    // with something left.
                    var taken = 0m;
                    for (var i = lots.Count - 1; i >= head && taken < amount; i--)
                    {
                        var part = Math.Min(amount - taken, lots[i].Remaining);
                        lots[i].Remaining -= part;
                        taken += part;
                    }
                    while (lots.Count > head && lots[^1].Remaining == 0)
                    {
                        held.Remove(lots[^1].EarningId);
                        lots.RemoveAt(lots.Count - 1);
                    }
                    return taken;
                }
            }
        }

        public WalletPools Result() => new(
            bought,
            lots.Skip(head)
                .Where(l => l.Remaining > 0)
                .Select(l => new HeldLot(l.EarningId, DateTime.SpecifyKind(l.AvailableAt, DateTimeKind.Utc), l.Remaining))
                .ToList(),
            released,
            deficit);
    }
}
