using Application.Wallet;
using Domain.Repositories;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The refund clawback's plan (#27): the deficit is taken back from every held earning the buyer paid for, newest payment
/// first, then from the payments of each account that left below zero, at most three accounts further, each account's
/// payments walked once. In memory; the same against SQL Server: Integration/EarningsHoldTests.
/// </summary>
public class ClawbackPlanTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Wallet balances and the held earnings each account paid for.</summary>
    private sealed class World
    {
        private readonly Dictionary<string, List<HeldEarning>> paidBy = new();
        private DateTime clock = T0;

        public Dictionary<string, decimal> Balances { get; } = new();

        /// <summary>Wallets the plan may not read (not locked).</summary>
        public HashSet<string> Unreadable { get; } = new();

        /// <summary><paramref name="payer"/> gave <paramref name="author"/> this much, still on hold.</summary>
        public HeldEarning Pay(string payer, string author, decimal amount, decimal remaining = -1)
        {
            clock = clock.AddMinutes(1);
            var earning = new HeldEarning(Guid.NewGuid(), author, remaining < 0 ? amount : remaining, null, null, null, clock);
            if (!paidBy.TryGetValue(payer, out var list))
            {
                paidBy[payer] = list = new List<HeldEarning>();
            }
            list.Insert(0, earning); // newest first
            return earning;
        }

        public Task<EarningsClawback.Plan> Plan(string buyer, decimal refunded) => EarningsClawback.PlanAsync(buyer, refunded,
            id => Task.FromResult(Unreadable.Contains(id) ? null : (decimal?)Balances.GetValueOrDefault(id)),
            payer => Task.FromResult<IReadOnlyList<HeldEarning>>(paidBy.GetValueOrDefault(payer) ?? []));
    }

    [Fact]
    public void The_deficit_is_how_far_below_zero_the_refund_took_the_balance_and_no_more_than_the_refund()
    {
        Assert.Equal(0, EarningsClawback.Deficit(1000, 200));
        Assert.Equal(700, EarningsClawback.Deficit(1000, -700));
        Assert.Equal(1000, EarningsClawback.Deficit(1000, -1500)); // 500 was owed already
    }

    [Fact]
    public async Task A_refund_the_balance_covers_takes_nothing_back()
    {
        var world = new World { Balances = { ["buyer"] = 2200 } };
        world.Pay("buyer", "author", 800);

        var plan = await world.Plan("buyer", 1000);

        Assert.Equal((0m, 0), (plan.Deficit, plan.Reversals.Count));
        Assert.Equal(["buyer"], plan.Wallets);
        Assert.Equal(1200m, plan.BalancesAfter["buyer"]);
    }

    [Fact]
    public async Task The_buyers_payments_are_taken_back_newest_first_whenever_they_were_paid_up_to_the_deficit()
    {
        var world = new World { Balances = { ["buyer"] = 100, ["a"] = 400, ["b"] = 500, ["c"] = 200 } };
        var first = world.Pay("buyer", "a", 400);
        var second = world.Pay("buyer", "b", 500);
        var third = world.Pay("buyer", "c", 200);

        var plan = await world.Plan("buyer", 1000); // 100 - 1000 = -900

        Assert.Equal(900m, plan.Deficit);
        Assert.Equal([(third, 200m), (second, 500m), (first, 200m)], plan.Reversals.Select(r => (r.Earning, r.Amount)));
        Assert.All(plan.Reversals, r => Assert.Equal(("buyer", 0), (r.PayerId, r.Depth)));
        Assert.Equal(["buyer", "c", "b", "a"], plan.Wallets);
        Assert.Equal((0m, 200m, 0m, 0m), (plan.BalancesAfter["buyer"], plan.BalancesAfter["a"], plan.BalancesAfter["b"], plan.BalancesAfter["c"]));
        Assert.Equal((900m, 0m), (plan.ReturnedToBuyer, plan.Uncovered));
    }

    [Fact]
    public async Task What_no_held_earning_covers_is_the_buyers_loss()
    {
        var world = new World { Balances = { ["buyer"] = 0, ["a"] = 300 } };
        world.Pay("buyer", "a", 300);

        var plan = await world.Plan("buyer", 1000);

        Assert.Equal((1000m, 300m, 700m), (plan.Deficit, plan.ReturnedToBuyer, plan.Uncovered));
        Assert.Equal(-700m, plan.BalancesAfter["buyer"]);
    }

    [Fact]
    public async Task An_author_left_below_zero_has_their_own_payments_taken_back_for_that_much()
    {
        // Scenario D: the buyer gave H 1000, H gave them on to C.
        var world = new World { Balances = { ["buyer"] = 0, ["h"] = 0, ["c"] = 1000 } };
        var toH = world.Pay("buyer", "h", 1000);
        var toC = world.Pay("h", "c", 1000);

        var plan = await world.Plan("buyer", 1000);

        Assert.Equal([(toH, "buyer", 0), (toC, "h", 1)], plan.Reversals.Select(r => (r.Earning, r.PayerId, r.Depth)));
        Assert.Equal((0m, 0m, 0m), (plan.BalancesAfter["buyer"], plan.BalancesAfter["h"], plan.BalancesAfter["c"]));
        Assert.Equal(["buyer", "h", "c"], plan.Wallets);
    }

    [Fact]
    public async Task Only_how_far_this_refund_took_an_author_below_zero_is_followed_not_a_debt_from_before()
    {
        var world = new World { Balances = { ["buyer"] = 0, ["h"] = -100, ["c"] = 5000 } };
        world.Pay("buyer", "h", 300);
        var toC = world.Pay("h", "c", 1000);

        var plan = await world.Plan("buyer", 300);

        Assert.Equal((toC, 300m), (plan.Reversals[1].Earning, plan.Reversals[1].Amount));
        Assert.Equal(-100m, plan.BalancesAfter["h"]);
    }

    [Fact]
    public async Task An_author_who_had_enough_of_their_own_is_not_followed()
    {
        var world = new World { Balances = { ["buyer"] = 0, ["h"] = 1500, ["c"] = 1000 } };
        world.Pay("buyer", "h", 1000);
        world.Pay("h", "c", 1000);

        var plan = await world.Plan("buyer", 1000);

        Assert.Single(plan.Reversals);
        Assert.Equal(["buyer", "h"], plan.Wallets);
    }

    [Fact]
    public async Task The_cascade_follows_three_accounts_past_the_buyers_payments_and_no_further()
    {
        var world = new World { Balances = { ["buyer"] = 0, ["h1"] = 0, ["h2"] = 0, ["h3"] = 0, ["h4"] = 0, ["c"] = 1000 } };
        var from = "buyer";
        foreach (var next in new[] { "h1", "h2", "h3", "h4", "c" })
        {
            world.Pay(from, next, 1000);
            from = next;
        }

        var plan = await world.Plan("buyer", 1000);

        Assert.Equal([0, 1, 2, 3], plan.Reversals.Select(r => r.Depth));
        Assert.Equal(EarningsClawback.MaxCascadeDepth, plan.Reversals.Max(r => r.Depth));
        Assert.Equal([0m, 0m, 0m, 0m, -1000m], new[] { "buyer", "h1", "h2", "h3", "h4" }.Select(u => plan.BalancesAfter[u]));
        Assert.DoesNotContain("c", plan.Wallets);
    }

    [Fact]
    public async Task Each_account_is_walked_once_so_a_cycle_ends()
    {
        // The buyer gave A 1000, A gave it back, and the buyer spent it where it can't be taken back.
        var world = new World { Balances = { ["buyer"] = 0, ["a"] = 0 } };
        var toA = world.Pay("buyer", "a", 1000);
        var back = world.Pay("a", "buyer", 1000);

        var plan = await world.Plan("buyer", 1000);

        // A's gift back is taken back from the buyer, whose payments aren't walked again: the loss stays with the buyer.
        Assert.Equal([(toA, "buyer", 0), (back, "a", 1)], plan.Reversals.Select(r => (r.Earning, r.PayerId, r.Depth)));
        Assert.Equal((-1000m, 0m), (plan.BalancesAfter["buyer"], plan.BalancesAfter["a"]));
    }

    [Fact]
    public async Task Two_ways_to_the_same_account_take_back_both_earnings_once_each()
    {
        var world = new World { Balances = { ["buyer"] = 0, ["a"] = 0, ["h"] = 0, ["c"] = 1000 } };
        world.Pay("buyer", "a", 500);
        world.Pay("buyer", "h", 500);
        var fromA = world.Pay("a", "c", 500);
        var fromH = world.Pay("h", "c", 500);

        var plan = await world.Plan("buyer", 1000);

        Assert.Equal(4, plan.Reversals.Count);
        Assert.Equal(new HashSet<HeldEarning> { fromA, fromH }, plan.Reversals.Where(r => r.Depth == 1).Select(r => r.Earning).ToHashSet());
        Assert.All(plan.BalancesAfter.Values, balance => Assert.Equal(0m, balance));
    }

    [Fact]
    public async Task What_was_taken_back_of_an_earning_already_is_not_taken_again()
    {
        var world = new World { Balances = { ["buyer"] = 0, ["a"] = 1000 } };
        var older = world.Pay("buyer", "a", 400);
        world.Pay("buyer", "a", 1000, remaining: 0); // an earlier refund took all of this one

        var plan = await world.Plan("buyer", 1000);

        var reversal = Assert.Single(plan.Reversals);
        Assert.Equal((older, 400m), (reversal.Earning, reversal.Amount));
        Assert.Equal(600m, plan.Uncovered);
    }

    [Fact]
    public async Task A_wallet_the_plan_may_not_read_stops_it_and_is_named()
    {
        var world = new World { Balances = { ["buyer"] = 0, ["h"] = 0, ["c"] = 1000 }, Unreadable = { "c" } };
        world.Pay("buyer", "h", 1000);
        world.Pay("h", "c", 1000);

        var plan = await world.Plan("buyer", 1000);

        Assert.Equal(["c"], plan.Missing);
        Assert.DoesNotContain(plan.Reversals, r => r.Earning.AuthorId == "c");
    }
}
