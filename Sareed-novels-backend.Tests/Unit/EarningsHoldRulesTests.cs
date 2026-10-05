using Application.Services;
using Application.Wallet;
using Domain.Constants;
using Domain.Repositories;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The arithmetic of #22 and #27: what can be withdrawn from the pools (rule 3), how a refund's deficit is taken back from
/// held earnings (rule 4), and the Arabic refusal. How the pools come from the ledger: WalletPoolsTests; the clawback's
/// cascade: ClawbackPlanTests; the same rules against SQL Server: Integration/EarningsHoldTests.
/// </summary>
public class EarningsHoldRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static WithdrawableBalance Wallet(decimal balance, decimal released = 0, decimal pendingWithdrawals = 0,
        decimal pendingEarnings = 0, DateTime? nextReleaseAt = null, int holdDays = 30, decimal deficit = 0) =>
        new(balance, Math.Max(0, balance - released - pendingEarnings), released, pendingEarnings, nextReleaseAt, deficit,
            pendingWithdrawals, holdDays, Now, TotalEarned: released + pendingEarnings);

    // ===== Rule 3: what is withdrawable =====

    [Fact]
    public void Bought_points_are_never_withdrawable_however_large_the_balance()
    {
        Assert.Equal(0, Wallet(50_000).Withdrawable);
        Assert.Equal(0, Wallet(50_000).Payable);
        // Earnings on hold don't count yet either.
        Assert.Equal(0, Wallet(50_000, pendingEarnings: 1000, nextReleaseAt: Now.AddDays(3)).Withdrawable);
    }

    [Fact]
    public void Released_earnings_are_withdrawable_less_what_pending_requests_reserve()
    {
        var wallet = Wallet(4000, released: 3500, pendingWithdrawals: 1500);

        Assert.Equal(3500, wallet.Payable);
        Assert.Equal(2000, wallet.Withdrawable);
    }

    [Fact]
    public void Pending_requests_reserve_the_balance_as_well_as_the_earnings()
    {
        // 2000 earned, 500 spent, 1000 requested: once that is paid the balance holds 500.
        var wallet = Wallet(1500, released: 1500, pendingWithdrawals: 1000);

        Assert.Equal(500, wallet.Withdrawable);
        // An approval checks against what is left before any request is paid.
        Assert.Equal(1500, wallet.Payable);
    }

    [Fact]
    public void Never_more_than_the_balance()
    {
        // The pools add up to the balance, so this can't happen; if it ever did, the balance is the limit.
        Assert.Equal(1200, Wallet(1200, released: 1500).Payable);
    }

    [Theory]
    [InlineData(-700)]
    [InlineData(0)]
    public void An_empty_or_negative_balance_can_withdraw_nothing(decimal balance)
    {
        Assert.Equal(0, Wallet(balance, released: 3000).Withdrawable);
        Assert.Equal(0, Wallet(balance, released: 3000).Payable);
    }

    [Fact]
    public void Never_below_zero()
    {
        // Requests made before #22 against bought points: more reserved than there is to pay.
        Assert.Equal(0, Wallet(3000, released: 1000, pendingWithdrawals: 2000).Withdrawable);
    }

    [Fact]
    public void The_record_takes_its_amounts_from_the_pools()
    {
        LedgerEntry[] ledger =
        [
            new(Guid.NewGuid(), TransactionType.GiftReceived, 1000, 0, 1000, Now.AddDays(-40), Now.AddDays(-10)),
            new(Guid.NewGuid(), TransactionType.GiftReceived, 800, 1000, 1800, Now.AddDays(-2), Now.AddDays(28))
        ];
        var pools = WalletPools.Fold(1800, ledger, Now);

        var wallet = WithdrawableBalance.From(1800, pools, 300, 30, Now, Earnings.Net(ledger));

        Assert.Equal((1800m, 0m, 1000m, 800m, 0m, 300m), (wallet.Balance, wallet.Bought, wallet.Released, wallet.PendingEarnings,
            wallet.Deficit, wallet.PendingWithdrawals));
        Assert.Equal(1800m, wallet.TotalEarned);
        Assert.Equal(Now.AddDays(28), wallet.NextReleaseAt);
        Assert.Equal((1000m, 700m), (wallet.Payable, wallet.Withdrawable));
    }

    [Fact]
    public void Only_gifts_and_privilege_subscriptions_received_are_earnings()
    {
        Assert.Equal([TransactionType.GiftReceived, TransactionType.PrivilegeRevenue], TransactionType.Earnings);
        Assert.All(TransactionType.Earnings, type => Assert.True(TransactionType.IsEarning(type)));
        foreach (var type in new[]
                 {
                     TransactionType.RechargeApproved, TransactionType.PlayPurchase, TransactionType.GiftSent,
                     TransactionType.PrivilegeSubscription, TransactionType.EarningReversed, TransactionType.PlayRefund, "Recharge"
                 })
        {
            Assert.False(TransactionType.IsEarning(type), type);
        }
    }

    // ===== Rule 4: the refund clawback =====

    private static HeldEarning Held(decimal remaining, string author = "author") =>
        new(Guid.NewGuid(), author, remaining, null, null, null, Now);

    [Theory]
    [InlineData(1000, 200, 0)] // the balance covered it
    [InlineData(1000, 0, 0)]
    [InlineData(1000, -700, 700)]
    [InlineData(1000, -1000, 1000)]
    [InlineData(1000, -1500, 1000)] // 500 was owed already: only this refund's part
    public void The_deficit_is_how_far_below_zero_this_refund_took_the_balance(decimal refunded, decimal balanceAfter, decimal deficit) =>
        Assert.Equal(deficit, EarningsClawback.Deficit(refunded, balanceAfter));

    [Fact]
    public void Newest_payments_are_taken_back_first_and_only_up_to_the_deficit()
    {
        var (newest, middle, oldest) = (Held(300, "a"), Held(500, "b"), Held(800, "c"));

        var reversals = EarningsClawback.Allocate(700, [newest, middle, oldest]);

        Assert.Equal(new[] { (newest, 300m), (middle, 400m) }, reversals);
    }

    [Fact]
    public void Earnings_with_nothing_left_are_skipped()
    {
        var (reversedAlready, partly, whole) = (Held(0), Held(50), Held(500));

        var reversals = EarningsClawback.Allocate(100, [reversedAlready, partly, whole]);

        Assert.Equal(new[] { (partly, 50m), (whole, 50m) }, reversals);
    }

    [Fact]
    public void A_deficit_larger_than_everything_on_hold_takes_all_of_it_and_nothing_takes_nothing()
    {
        var (a, b) = (Held(200), Held(300));

        Assert.Equal(new[] { (a, 200m), (b, 300m) }, EarningsClawback.Allocate(10_000, [a, b]));
        Assert.Empty(EarningsClawback.Allocate(0, [a, b]));
        Assert.Empty(EarningsClawback.Allocate(500, []));
    }

    // ===== The refusal, in Arabic =====

    [Fact]
    public void The_refusal_says_what_can_be_withdrawn_now_and_when_more_is_released()
    {
        var wallet = Wallet(5000, released: 1200, pendingEarnings: 800, nextReleaseAt: Now.AddDays(4).AddHours(3));

        Assert.Equal(
            "يمكنك سحب 1200 نقطة فقط الآن، وتصبح أرباحك التالية قابلة للسحب خلال 5 أيام. " +
            "تُسحب أرباح الهدايا واشتراكات الوصول المبكر وحدها، بعد 30 يومًا من استلامها، أما النقاط المشحونة أو المشتراة فلا تُسحب.",
            WithdrawalMessages.NotWithdrawable(wallet));
    }

    [Fact]
    public void The_refusal_without_earnings_explains_the_rule()
    {
        Assert.Equal(
            "لا توجد نقاط قابلة للسحب الآن. " +
            "تُسحب أرباح الهدايا واشتراكات الوصول المبكر وحدها، بعد 30 يومًا من استلامها، أما النقاط المشحونة أو المشتراة فلا تُسحب.",
            WithdrawalMessages.NotWithdrawable(Wallet(50_000)));

        Assert.Equal(
            "لا توجد نقاط قابلة للسحب الآن، وتصبح أرباحك التالية قابلة للسحب خلال يوم واحد. " +
            "تُسحب أرباح الهدايا واشتراكات الوصول المبكر وحدها، بعد 7 أيام من استلامها، أما النقاط المشحونة أو المشتراة فلا تُسحب.",
            WithdrawalMessages.NotWithdrawable(Wallet(300, pendingEarnings: 300, nextReleaseAt: Now.AddMinutes(10), holdDays: 7)));

        Assert.EndsWith("تُسحب أرباح الهدايا واشتراكات الوصول المبكر وحدها، أما النقاط المشحونة أو المشتراة فلا تُسحب.",
            WithdrawalMessages.NotWithdrawable(Wallet(300, holdDays: 0)));
    }

    [Fact]
    public void The_admin_is_told_what_the_member_can_be_paid()
    {
        Assert.Equal("رصيد المستخدم القابل للسحب لا يكفي لهذا الطلب: القابل للسحب الآن 400 نقطة.",
            WithdrawalMessages.NotPayable(Wallet(400, released: 1500, pendingWithdrawals: 1000)));
    }

    [Theory]
    [InlineData(1, "يوم واحد")]
    [InlineData(2, "يومين")]
    [InlineData(3, "3 أيام")]
    [InlineData(10, "10 أيام")]
    [InlineData(11, "11 يومًا")]
    [InlineData(30, "30 يومًا")]
    [InlineData(99, "99 يومًا")]
    [InlineData(100, "100 يوم")]
    [InlineData(103, "103 أيام")]
    [InlineData(365, "365 يومًا")]
    public void Days_are_counted_the_arabic_way(int days, string expected) =>
        Assert.Equal(expected, WithdrawalMessages.Days(days));

    [Fact]
    public void Days_until_a_release_round_up()
    {
        Assert.Equal(1, WithdrawalMessages.DaysUntil(Now.AddSeconds(1), Now));
        Assert.Equal(1, WithdrawalMessages.DaysUntil(Now.AddDays(1), Now));
        Assert.Equal(2, WithdrawalMessages.DaysUntil(Now.AddDays(1).AddMinutes(1), Now));
        Assert.Equal(30, WithdrawalMessages.DaysUntil(Now.AddDays(30), Now));
    }
}
