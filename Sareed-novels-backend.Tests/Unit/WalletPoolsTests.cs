using Application.Wallet;
using Domain.Constants;
using Domain.Repositories;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The pools a ledger makes (#27), ledger rows in, pools out: bought points never become withdrawable, earnings are held
/// until their release, spending comes out of bought points, then held earnings, then released ones, and refunds,
/// reversals and withdrawals take from where they must. The same through the API and SQL Server:
/// Integration/EarningsHoldTests.
/// </summary>
public class WalletPoolsTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Hold = TimeSpan.FromDays(30);

    /// <summary>A user's ledger as the API writes it: each row starts at the balance the one before ended at.</summary>
    private sealed class Ledger
    {
        public List<LedgerEntry> Rows { get; } = new();
        public decimal Balance { get; private set; }

        public Ledger(decimal opening = 0) => Balance = opening;

        public LedgerEntry Add(string type, decimal amount, DateTime at, DateTime? availableAt = null, Guid? reversed = null)
        {
            var row = new LedgerEntry(Guid.NewGuid(), type, amount, Balance, Balance + amount, at, availableAt, reversed);
            Balance += amount;
            Rows.Add(row);
            return row;
        }

        public LedgerEntry Earn(decimal amount, DateTime at, string type = TransactionType.GiftReceived) =>
            Add(type, amount, at, at + Hold);

        public LedgerEntry Spend(decimal amount, DateTime at, string type = TransactionType.GiftSent) => Add(type, -amount, at);

        public WalletPools Pools(DateTime asOf) => WalletPools.Fold(Balance, Rows, asOf);
    }

    private static void AssertPools(WalletPools pools, decimal bought, decimal held, decimal released, decimal deficit = 0)
    {
        Assert.Equal((bought, held, released, deficit), (pools.Bought, pools.PendingEarnings, pools.Released, pools.Deficit));
        Assert.Equal(bought + held + released - deficit, pools.Balance);
    }

    // ===== Where points come from =====

    [Fact]
    public void Top_ups_and_play_packs_are_bought_and_never_released()
    {
        var ledger = new Ledger();
        ledger.Add(TransactionType.RechargeApproved, 5000, T0);
        ledger.Add(TransactionType.PlayPurchase, 1000, T0.AddMinutes(1));

        AssertPools(ledger.Pools(T0.AddDays(365)), bought: 6000, held: 0, released: 0);
    }

    [Fact]
    public void An_earning_is_held_until_its_release_and_released_from_then_on()
    {
        var ledger = new Ledger();
        var gift = ledger.Earn(1000, T0);
        ledger.Earn(500, T0.AddDays(3), TransactionType.PrivilegeRevenue);

        var held = ledger.Pools(T0.AddDays(30) - TimeSpan.FromTicks(1));
        AssertPools(held, bought: 0, held: 1500, released: 0);
        Assert.Equal(T0 + Hold, held.NextReleaseAt);
        Assert.Equal(DateTimeKind.Utc, held.NextReleaseAt!.Value.Kind);
        Assert.Equal(gift.Id, held.Held[0].EarningId);

        var released = ledger.Pools(T0 + Hold);
        AssertPools(released, bought: 0, held: 500, released: 1000);
        Assert.Equal(T0.AddDays(33), released.NextReleaseAt);

        AssertPools(ledger.Pools(T0.AddDays(33)), bought: 0, held: 0, released: 1500);
        Assert.Null(ledger.Pools(T0.AddDays(33)).NextReleaseAt);
    }

    [Fact]
    public void Earnings_from_before_the_hold_are_released_at_once()
    {
        // The #22 migration set AvailableAt = CreatedAt on every earning written before it.
        var ledger = new Ledger();
        ledger.Add(TransactionType.GiftReceived, 700, T0, availableAt: T0);

        AssertPools(ledger.Pools(T0.AddMinutes(1)), bought: 0, held: 0, released: 700);
    }

    [Fact]
    public void An_earning_without_a_release_date_is_never_withdrawable()
    {
        // The database refuses one (a check constraint); if one ever turned up it would count as bought.
        var ledger = new Ledger();
        ledger.Add(TransactionType.GiftReceived, 700, T0);

        AssertPools(ledger.Pools(T0.AddDays(90)), bought: 700, held: 0, released: 0);
    }

    [Fact]
    public void A_balance_from_before_the_ledger_is_bought_when_positive_and_owed_when_negative()
    {
        // Production's ledger started empty: every balance then is from before it.
        AssertPools(WalletPools.Fold(5000, [], T0), bought: 5000, held: 0, released: 0);
        AssertPools(WalletPools.Fold(-300, [], T0), bought: 0, held: 0, released: 0, deficit: 300);

        // With a ledger, what the balance holds beyond its rows.
        var ledger = new Ledger(opening: 2000);
        ledger.Earn(1000, T0);
        AssertPools(ledger.Pools(T0 + Hold), bought: 2000, held: 0, released: 1000);
    }

    // ===== Spending =====

    [Fact]
    public void Spending_comes_out_of_bought_points_then_held_earnings_then_released_ones()
    {
        var ledger = new Ledger();
        ledger.Earn(1000, T0); // released by the time it is spent
        ledger.Add(TransactionType.RechargeApproved, 2000, T0.AddDays(31));
        ledger.Earn(500, T0.AddDays(32));

        ledger.Spend(1800, T0.AddDays(33));
        AssertPools(ledger.Pools(T0.AddDays(33)), bought: 200, held: 500, released: 1000);
        ledger.Spend(500, T0.AddDays(34), TransactionType.PrivilegeSubscription);
        AssertPools(ledger.Pools(T0.AddDays(34)), bought: 0, held: 200, released: 1000);
        ledger.Spend(700, T0.AddDays(35));
        AssertPools(ledger.Pools(T0.AddDays(35)), bought: 0, held: 0, released: 500);
    }

    [Fact]
    public void Of_the_held_earnings_the_one_released_last_is_spent_first()
    {
        var ledger = new Ledger();
        var older = ledger.Earn(1000, T0);
        ledger.Earn(1000, T0.AddDays(10));

        ledger.Spend(600, T0.AddDays(11));

        var pools = ledger.Pools(T0.AddDays(11));
        Assert.Equal([(older.Id, 1000m), (pools.Held[1].EarningId, 400m)], pools.Held.Select(l => (l.EarningId, l.Remaining)));
        Assert.Equal(T0 + Hold, pools.NextReleaseAt); // the older earning keeps its release date
        AssertPools(ledger.Pools(T0 + Hold), bought: 0, held: 400, released: 1000);
    }

    [Fact]
    public void Scenario_G_a_gift_to_an_author_who_spent_their_released_earnings_is_held()
    {
        var ledger = new Ledger();
        ledger.Earn(1000, T0);
        ledger.Spend(1000, T0.AddDays(31)); // gives all of it away
        ledger.Earn(1000, T0.AddDays(32)); // a fraudster's gift

        AssertPools(ledger.Pools(T0.AddDays(32).AddMinutes(1)), bought: 0, held: 1000, released: 0);
    }

    [Fact]
    public void Scenario_C_points_bought_after_giving_earnings_away_are_bought()
    {
        var ledger = new Ledger();
        ledger.Earn(1000, T0);
        ledger.Spend(1000, T0.AddDays(31));
        ledger.Add(TransactionType.PlayPurchase, 1000, T0.AddDays(32));

        AssertPools(ledger.Pools(T0.AddDays(32)), bought: 1000, held: 0, released: 0);
    }

    [Fact]
    public void Scenario_I_gifts_back_and_forth_leave_released_only_what_was_received_last()
    {
        var a = new Ledger();
        a.Add(TransactionType.RechargeApproved, 1000, T0);
        var at = T0;
        for (var round = 0; round < 5; round++)
        {
            a.Spend(1000, at = at.AddMinutes(1));
            a.Earn(1000, at = at.AddMinutes(1));
        }
        a.Add(TransactionType.PlayPurchase, 5000, at = at.Add(Hold));

        AssertPools(a.Pools(at), bought: 5000, held: 0, released: 1000);
    }

    // ===== Refunds and reversals =====

    [Fact]
    public void A_refund_is_paid_from_bought_then_held_then_released_and_the_rest_is_owed()
    {
        var ledger = new Ledger();
        ledger.Earn(300, T0); // released by the refund
        ledger.Earn(400, T0.AddDays(20));
        ledger.Add(TransactionType.PlayPurchase, 1000, T0.AddDays(21));
        ledger.Spend(800, T0.AddDays(22));

        ledger.Add(TransactionType.PlayRefund, -1000, T0.AddDays(31));

        // 200 bought, 400 held and 300 released cover 900: 100 is owed.
        AssertPools(ledger.Pools(T0.AddDays(31)), bought: 0, held: 0, released: 0, deficit: 100);
        Assert.Equal(-100m, ledger.Balance);
    }

    [Fact]
    public void Taking_an_earning_back_takes_that_very_earning_while_it_is_held()
    {
        var ledger = new Ledger();
        ledger.Add(TransactionType.RechargeApproved, 500, T0);
        var gift = ledger.Earn(1000, T0.AddMinutes(1));
        var other = ledger.Earn(700, T0.AddMinutes(2));

        ledger.Add(TransactionType.EarningReversed, -1000, T0.AddDays(3), reversed: gift.Id);

        var pools = ledger.Pools(T0.AddDays(3));
        AssertPools(pools, bought: 500, held: 700, released: 0);
        Assert.Equal(other.Id, Assert.Single(pools.Held).EarningId);
    }

    [Fact]
    public void What_was_spent_of_an_earning_taken_back_comes_out_like_any_debit_and_the_rest_is_owed()
    {
        var ledger = new Ledger();
        ledger.Earn(400, T0); // released by then
        var gift = ledger.Earn(1000, T0.AddDays(31));
        ledger.Spend(1000, T0.AddDays(31).AddMinutes(1)); // all of the held gift

        ledger.Add(TransactionType.EarningReversed, -1000, T0.AddDays(32), reversed: gift.Id);

        AssertPools(ledger.Pools(T0.AddDays(32)), bought: 0, held: 0, released: 0, deficit: 600);
    }

    [Fact]
    public void Points_a_reversal_gives_back_to_whom_paid_are_bought()
    {
        var ledger = new Ledger();
        ledger.Add(TransactionType.EarningReversed, 1000, T0);

        AssertPools(ledger.Pools(T0.AddDays(90)), bought: 1000, held: 0, released: 0);
    }

    [Fact]
    public void Credits_pay_what_is_owed_first()
    {
        var ledger = new Ledger();
        ledger.Add(TransactionType.PlayPurchase, 1000, T0);
        ledger.Spend(700, T0.AddMinutes(1));
        ledger.Add(TransactionType.PlayRefund, -1000, T0.AddDays(31)); // 700 owed

        ledger.Earn(1000, T0.AddDays(32)); // 700 of it pays the debt
        ledger.Add(TransactionType.RechargeApproved, 5000, T0.AddDays(33));

        AssertPools(ledger.Pools(T0.AddDays(62)), bought: 5000, held: 0, released: 300);
    }

    // ===== Withdrawals and forfeits =====

    [Fact]
    public void An_approved_withdrawal_comes_out_of_released_earnings()
    {
        var ledger = new Ledger();
        ledger.Add(TransactionType.RechargeApproved, 2000, T0);
        ledger.Earn(1500, T0);

        ledger.Add(TransactionType.WithdrawalApproved, -1000, T0.AddDays(31));

        AssertPools(ledger.Pools(T0.AddDays(31)), bought: 2000, held: 0, released: 500);
    }

    [Fact]
    public void A_withdrawal_approved_before_the_hold_existed_does_not_count_against_later_earnings()
    {
        // Before #22 any balance could be withdrawn: 2000 of a 5000 top-up were paid out.
        var ledger = new Ledger();
        ledger.Add(TransactionType.RechargeApproved, 5000, T0);
        ledger.Add(TransactionType.WithdrawalApproved, -2000, T0.AddDays(10));
        ledger.Earn(1000, T0.AddDays(20));

        AssertPools(ledger.Pools(T0.AddDays(50)), bought: 3000, held: 0, released: 1000);
    }

    [Fact]
    public void A_forfeited_balance_takes_everything_and_clearing_a_debt_pays_it()
    {
        var ledger = new Ledger();
        ledger.Add(TransactionType.RechargeApproved, 300, T0);
        ledger.Earn(1000, T0.AddDays(1));
        ledger.Earn(500, T0.AddDays(40));
        ledger.Add(TransactionType.BalanceForfeited, -1800, T0.AddDays(41));
        AssertPools(ledger.Pools(T0.AddDays(90)), bought: 0, held: 0, released: 0);

        var owing = new Ledger(opening: -400);
        owing.Add(TransactionType.BalanceForfeited, 400, T0);
        AssertPools(owing.Pools(T0), bought: 0, held: 0, released: 0);
    }

    // ===== Order =====

    [Fact]
    public void Rows_of_one_moment_follow_the_balances_they_recorded()
    {
        // A top-up and a gift at the same instant (a test clock): the gift started from the balance the top-up left.
        var ledger = new Ledger();
        ledger.Earn(1500, T0);
        var topUp = ledger.Add(TransactionType.RechargeApproved, 2000, T0.AddDays(31));
        var gift = ledger.Spend(1800, T0.AddDays(31));

        var reversedOrder = new[] { ledger.Rows[0], gift, topUp };
        AssertPools(WalletPools.Fold(ledger.Balance, reversedOrder, T0.AddDays(31)), bought: 200, held: 0, released: 1500);

        // And a gift sent just before a top-up of the same instant came out of the released earnings.
        var spentFirst = new Ledger();
        spentFirst.Earn(1500, T0);
        var spend = spentFirst.Spend(500, T0.AddDays(31));
        var then = spentFirst.Add(TransactionType.RechargeApproved, 2000, T0.AddDays(31));
        AssertPools(WalletPools.Fold(spentFirst.Balance, [spentFirst.Rows[0], then, spend], T0.AddDays(31)), bought: 2000, held: 0,
            released: 1000);
    }

    [Fact]
    public void A_row_stamped_after_the_moment_asked_about_releases_nothing_early()
    {
        var ledger = new Ledger();
        ledger.Earn(1000, T0);
        ledger.Spend(100, T0.AddDays(31)); // a clock ahead of this one

        AssertPools(ledger.Pools(T0.AddDays(29)), bought: 0, held: 900, released: 0);
    }

    /// <summary>The rules written out plainly, row by row, to check the fold against.</summary>
    private static (decimal Bought, List<(Guid Id, DateTime At, decimal Left)> Held, decimal Released, decimal Deficit) Reference(
        decimal balance, IReadOnlyList<LedgerEntry> rows, DateTime asOf)
    {
        decimal bought = 0, released = 0, deficit = 0;
        var lots = new List<(Guid Id, DateTime At, decimal Left, int Seq)>();
        var seq = 0;
        var opening = balance - rows.Sum(r => r.Amount);
        if (opening > 0) bought = opening; else deficit = -opening;

        void Release(DateTime t)
        {
            released += lots.Where(l => l.At <= t).Sum(l => l.Left);
            lots.RemoveAll(l => l.At <= t);
        }

        decimal Pay(decimal x)
        {
            var paid = Math.Min(x, deficit);
            deficit -= paid;
            return x - paid;
        }

        void Debit(decimal x, bool withdrawal)
        {
            decimal FromHeld(decimal want)
            {
                var got = 0m;
                foreach (var i in lots.Select((l, i) => (l, i)).OrderByDescending(p => p.l.At).ThenByDescending(p => p.l.Seq).Select(p => p.i).ToList())
                {
                    var part = Math.Min(want - got, lots[i].Left);
                    lots[i] = lots[i] with { Left = lots[i].Left - part };
                    got += part;
                }
                return got;
            }

            foreach (var pool in withdrawal ? new[] { 'r', 'b', 'h' } : new[] { 'b', 'h', 'r' })
            {
                var take = pool switch
                {
                    'b' => Math.Min(x, bought),
                    'r' => Math.Min(x, released),
                    _ => FromHeld(x)
                };
                if (pool == 'b') bought -= take;
                if (pool == 'r') released -= take;
                x -= take;
            }
            deficit += x;
        }

        foreach (var row in rows.OrderBy(r => r.CreatedAt)) // the tests give every row its own instant
        {
            var at = row.CreatedAt < asOf ? row.CreatedAt : asOf;
            Release(at);
            if (row.Amount > 0 && TransactionType.IsEarning(row.Type))
            {
                var left = Pay(row.Amount);
                if (left > 0 && row.AvailableAt > at) lots.Add((row.Id, row.AvailableAt!.Value, left, seq++));
                else if (left > 0) released += left;
            }
            else if (row.Amount > 0)
            {
                bought += Pay(row.Amount);
            }
            else if (row.Type == TransactionType.EarningReversed)
            {
                var x = -row.Amount;
                var i = lots.FindIndex(l => l.Id == row.ReversedTransactionId);
                if (i >= 0)
                {
                    var taken = Math.Min(x, lots[i].Left);
                    lots[i] = lots[i] with { Left = lots[i].Left - taken };
                    x -= taken;
                }
                Debit(x, withdrawal: false);
            }
            else
            {
                Debit(-row.Amount, withdrawal: row.Type == TransactionType.WithdrawalApproved);
            }
        }
        Release(asOf);
        return (bought, lots.Where(l => l.Left > 0).OrderBy(l => l.At).ThenBy(l => l.Seq).Select(l => (l.Id, l.At, l.Left)).ToList(),
            released, deficit);
    }

    [Fact]
    public void The_fold_takes_from_the_same_pools_as_the_rules_written_out_plainly()
    {
        var random = new Random(2027);
        for (var run = 0; run < 300; run++)
        {
            var ledger = new Ledger(opening: random.Next(-300, 800));
            var at = T0;
            var earnings = new List<LedgerEntry>();
            for (var step = 0; step < 60; step++)
            {
                at = at.AddMinutes(random.Next(1, 60 * 24 * 4));
                var amount = random.Next(1, 30) * 10m;
                switch (random.Next(9))
                {
                    case 0: ledger.Add(TransactionType.RechargeApproved, amount, at); break;
                    case 1: ledger.Add(TransactionType.EarningReversed, amount, at); break; // given back to whom paid
                    case 2 or 3:
                        // Holds of 30 days mostly, sometimes shorter or longer: lots don't always arrive in release order.
                        var hold = random.Next(4) == 0 ? TimeSpan.FromDays(random.Next(0, 60)) : Hold;
                        earnings.Add(ledger.Add(TransactionType.GiftReceived, amount, at, at + hold));
                        break;
                    case 4 or 5: ledger.Spend(amount, at); break;
                    case 6: ledger.Add(TransactionType.PlayRefund, -amount, at); break;
                    case 7 when earnings.Count > 0:
                        ledger.Add(TransactionType.EarningReversed, -amount, at, reversed: earnings[random.Next(earnings.Count)].Id);
                        break;
                    case 8: ledger.Add(TransactionType.WithdrawalApproved, -amount, at); break;
                }
            }

            foreach (var asOf in new[] { at.AddDays(-10), at, at.AddDays(45) })
            {
                var pools = ledger.Pools(asOf);
                var expected = Reference(ledger.Balance, ledger.Rows, asOf);
                Assert.Equal((expected.Bought, expected.Released, expected.Deficit), (pools.Bought, pools.Released, pools.Deficit));
                Assert.Equal(expected.Held, pools.Held.Select(l => (l.EarningId, l.AvailableAt, l.Remaining)).ToList());
            }
        }
    }

    [Fact]
    public void The_pools_always_add_up_to_the_balance_and_hold_nothing_while_something_is_owed()
    {
        var random = new Random(27);
        for (var run = 0; run < 200; run++)
        {
            var ledger = new Ledger(opening: random.Next(-500, 1500));
            var at = T0;
            var earnings = new List<LedgerEntry>();
            for (var step = 0; step < 40; step++)
            {
                at = at.AddHours(random.Next(1, 24 * 6));
                var amount = random.Next(1, 20) * 50m;
                switch (random.Next(8))
                {
                    case 0: ledger.Add(TransactionType.RechargeApproved, amount, at); break;
                    case 1: ledger.Add(TransactionType.PlayPurchase, amount, at); break;
                    case 2 or 3: earnings.Add(ledger.Earn(amount, at)); break;
                    case 4 when ledger.Balance >= amount: ledger.Spend(amount, at); break;
                    case 5: ledger.Add(TransactionType.PlayRefund, -amount, at); break;
                    case 6 when earnings.Count > 0:
                        var earning = earnings[random.Next(earnings.Count)];
                        ledger.Add(TransactionType.EarningReversed, -Math.Min(amount, earning.Amount), at, reversed: earning.Id);
                        break;
                    case 7 when ledger.Balance >= amount: ledger.Add(TransactionType.WithdrawalApproved, -amount, at); break;
                }

                var pools = ledger.Pools(at);
                Assert.Equal(ledger.Balance, pools.Balance);
                Assert.True(pools.Bought >= 0 && pools.Released >= 0 && pools.Deficit >= 0 && pools.Held.All(l => l.Remaining > 0));
                if (pools.Deficit > 0)
                {
                    Assert.Equal((0m, 0m, 0m), (pools.Bought, pools.PendingEarnings, pools.Released));
                }
            }
        }
    }
}
