using Application.Services;
using Application.Wallet;
using Domain.Constants;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The arithmetic of #22: what can be withdrawn (rule 3) and the Arabic refusal. The same rules against SQL Server:
/// Integration/EarningsHoldTests.
/// </summary>
public class EarningsHoldRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static WithdrawableBalance Wallet(decimal balance, decimal released = 0, decimal reversed = 0, decimal withdrawn = 0,
        decimal pendingWithdrawals = 0, decimal pendingEarnings = 0, DateTime? nextReleaseAt = null, int holdDays = 30) =>
        new(balance, released, reversed, withdrawn, pendingWithdrawals, pendingEarnings, nextReleaseAt, holdDays, Now);

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
    public void Spending_comes_out_of_bought_points_first_and_only_then_out_of_earnings()
    {
        // 1500 earned and 3500 bought.
        Assert.Equal(1500, Wallet(5000, released: 1500).Withdrawable);
        // 3000 spent: all of it bought points.
        Assert.Equal(1500, Wallet(2000, released: 1500).Withdrawable);
        // 3800 spent: the bought points ran out, 300 came out of earnings.
        Assert.Equal(1200, Wallet(1200, released: 1500).Withdrawable);
    }

    [Fact]
    public void Withdrawals_paid_or_pending_and_reversed_earnings_come_off()
    {
        var wallet = Wallet(4000, released: 5000, withdrawn: 1000, reversed: 500, pendingWithdrawals: 1500);

        Assert.Equal(3500, wallet.Payable); // min(4000, 5000 - 1000 - 500)
        Assert.Equal(2000, wallet.Withdrawable);
    }

    [Fact]
    public void Pending_requests_reserve_the_balance_as_well_as_the_earnings()
    {
        // 2000 earned, 500 spent, 1000 requested: once that is paid the balance holds 500.
        var wallet = Wallet(1500, released: 2000, pendingWithdrawals: 1000);

        Assert.Equal(500, wallet.Withdrawable);
        // An approval checks against what is left before any request is paid.
        Assert.Equal(1500, wallet.Payable);
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
        // Withdrew bought points before #22: more withdrawn than ever earned.
        Assert.Equal(0, Wallet(3000, released: 1000, withdrawn: 2000).Withdrawable);
        Assert.Equal(0, Wallet(3000, released: 1000, pendingWithdrawals: 2000).Withdrawable);
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
