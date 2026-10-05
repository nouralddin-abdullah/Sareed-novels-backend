using Application.Wallet;
using Domain.Constants;
using Domain.Repositories;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Which ledger rows are an author's earnings, and with which sign (#78): gifts and privilege subscriptions received, less
/// her side of a reversal; never the positive side, a payer's own points given back. The one rule for GET /api/wallet's
/// totalEarned and the earnings summary (EarningsBreakdownTests).
/// </summary>
public class EarningsTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 30, 0, DateTimeKind.Utc);

    private static LedgerEntry Row(string type, decimal amount) => new(Guid.NewGuid(), type, amount, 0, 0, Now.AddDays(-3));

    [Fact]
    public void Gifts_and_privileges_received_are_earnings_and_her_side_of_a_reversal_takes_back()
    {
        Assert.Equal(EarningKind.Gift, Earnings.KindOf(TransactionType.GiftReceived, 300));
        Assert.Equal(EarningKind.Privilege, Earnings.KindOf(TransactionType.PrivilegeRevenue, 150));
        Assert.Equal(EarningKind.Reversed, Earnings.KindOf(TransactionType.EarningReversed, -100));

        // The positive side of a reversal gives a payer their own points back: no earning, even for an author.
        Assert.Null(Earnings.KindOf(TransactionType.EarningReversed, 100));
        foreach (var type in new[]
                 {
                     TransactionType.GiftSent, TransactionType.PrivilegeSubscription, TransactionType.RechargeApproved,
                     TransactionType.PlayPurchase, TransactionType.PlayRefund, TransactionType.WithdrawalApproved,
                     TransactionType.BalanceForfeited, TransactionType.Refund, "Recharge", "Withdrawal"
                 })
        {
            Assert.Null(Earnings.KindOf(type, 500));
            Assert.Null(Earnings.KindOf(type, -500));
        }
    }

    [Fact]
    public void What_was_earned_in_all_is_earnings_less_her_reversals_and_nothing_else()
    {
        LedgerEntry[] ledger =
        [
            Row(TransactionType.GiftReceived, 300), Row(TransactionType.GiftReceived, 200), Row(TransactionType.PrivilegeRevenue, 150),
            Row(TransactionType.EarningReversed, -120), // her earning taken back
            Row(TransactionType.EarningReversed, 80), // her own payment given back
            Row(TransactionType.GiftSent, -400), Row(TransactionType.RechargeApproved, 1000), Row(TransactionType.PlayRefund, -1000),
            Row(TransactionType.WithdrawalApproved, -100), Row(TransactionType.PrivilegeSubscription, -150)
        ];

        Assert.Equal(530m, Earnings.Net(ledger));
        Assert.Equal(0m, Earnings.Net([]));
    }
}
