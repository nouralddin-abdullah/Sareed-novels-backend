using Application.Wallet;
using Domain.Entities;

namespace Application.Services;

/// <summary>What a ledger row was about, beyond its type: the novel, the gift and how many (PointTransaction's columns).</summary>
public sealed record TransactionDetails(Guid? NovelId = null, Guid? GiftId = null, int? GiftCount = null);

/// <summary>
/// What a user can withdraw (#22, #27). Only earnings (gifts and privilege subscriptions received) are ever paid out, once
/// their hold has ended; bought points (website top-ups, Google Play) are spent inside Sard only. The amounts come from
/// the user's pools (<see cref="WalletPools"/>): spending comes out of bought points first, then earnings on hold, then
/// released earnings, so what was spent never stays withdrawable.
/// </summary>
/// <param name="Balance">The wallet balance (may be negative after a refund).</param>
/// <param name="Bought">Bought points left (never withdrawable).</param>
/// <param name="Released">Earnings whose hold has ended, less what was spent, withdrawn or taken back of them.</param>
/// <param name="PendingEarnings">Earnings still on hold, less what was spent or taken back of them.</param>
/// <param name="NextReleaseAt">When the next of those becomes withdrawable (UTC); null when none is on hold.</param>
/// <param name="Deficit">What the user owes after a refund took more than they had (the balance below zero).</param>
/// <param name="PendingWithdrawals">Points of withdrawals requested and not decided yet: reserved, though not deducted
/// from the balance until approval.</param>
/// <param name="HoldDays">The hold (Wallet:EarningsHoldDays).</param>
/// <param name="AsOf">The moment this was computed (UTC).</param>
public sealed record WithdrawableBalance(
    decimal Balance,
    decimal Bought,
    decimal Released,
    decimal PendingEarnings,
    DateTime? NextReleaseAt,
    decimal Deficit,
    decimal PendingWithdrawals,
    int HoldDays,
    DateTime AsOf)
{
    /// <summary>
    /// What approving a withdrawal can pay out now: the released earnings, never more than the balance, never below 0.
    /// Pending requests don't count here: an approval pays one request against what is actually left, and the next one
    /// is checked again.
    /// </summary>
    public decimal Payable => Math.Max(0, Math.Min(Released, Balance));

    /// <summary>
    /// What a new request can ask for: <see cref="Payable"/> less the pending requests, never below 0. They haven't left
    /// the balance yet, so two requests can't use the same points.
    /// </summary>
    public decimal Withdrawable => Math.Max(0, Payable - PendingWithdrawals);

    public static WithdrawableBalance From(decimal balance, WalletPools pools, decimal pendingWithdrawals, int holdDays, DateTime asOf) =>
        new(balance, pools.Bought, pools.Released, pools.PendingEarnings, pools.NextReleaseAt, pools.Deficit, pendingWithdrawals,
            holdDays, asOf);
}

/// <summary>What a Google Play refund did (#22 rule 4, #27).</summary>
/// <param name="BuyerBalance">The buyer's balance after it (below zero when not all of it could be taken back).</param>
/// <param name="ReturnedToBuyer">What was taken back from the earnings the buyer's own payments became, and given back to them.</param>
/// <param name="TakenBack">What was taken back from earnings in all, down the cascade included.</param>
/// <param name="Reversals">How many earnings were taken back from.</param>
/// <param name="Uncovered">The buyer's deficit that no earning on hold could cover.</param>
public sealed record PlayRefundOutcome(decimal BuyerBalance, decimal ReturnedToBuyer, decimal TakenBack, int Reversals, decimal Uncovered);

/// <summary>
/// The clawback of a refund needs a wallet it didn't lock: the ledger changed between the plan read without locks and the
/// locks. Nothing was written; roll back and start again with <see cref="Wallets"/> locked too.
/// </summary>
public sealed class RefundWalletsChangedException(IReadOnlyList<string> wallets)
    : Exception($"The refund's clawback needs {wallets.Count} more wallet(s) than it locked")
{
    public IReadOnlyList<string> Wallets { get; } = wallets;
}

public interface IWalletService
{
    Task<UserWallet> GetOrCreateWalletAsync(string userId);

    /// <summary>
    /// Credits the user. An earning type (<see cref="Domain.Constants.TransactionType.Earnings"/>) is held: its row gets
    /// AvailableAt = now + Wallet:EarningsHoldDays.
    /// </summary>
    Task AddPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null, TransactionDetails? details = null);
    Task DeductPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null, TransactionDetails? details = null);
    Task<bool> HasSufficientBalanceAsync(string userId, decimal amount);
    Task SyncUserBalanceAsync(string userId);

    /// <summary>
    /// Atomically transfers points from one user to another within a transaction.
    /// Must be called within an existing transaction scope.
    /// Both rows share <paramref name="relatedRequestId"/> (a new id when none is given), which pairs a reader's payment
    /// with the author's earning for the refund clawback; an earning type on the receiving side is held.
    /// </summary>
    Task TransferPointsAsync(
        string fromUserId,
        string toUserId,
        decimal amount,
        string fromTransactionType,
        string toTransactionType,
        string fromDescription,
        string toDescription,
        Guid? relatedRequestId = null,
        TransactionDetails? details = null);

    /// <summary>
    /// What the user can withdraw now, and their earnings still on hold (#22, #27), without locks: for showing. A change
    /// committed while it reads counts as if it came before the ledger, which can only lower what it shows.
    /// </summary>
    Task<WithdrawableBalance> GetWithdrawableAsync(string userId);

    /// <summary>
    /// <see cref="GetWithdrawableAsync"/> under an update lock on the user's wallet row, held until the caller's
    /// transaction ends (call inside one): for deciding. Every change to the user's balance and ledger (spending, a
    /// withdrawal paid, a refund or its clawback) waits until then, so what is checked is still true when the request is
    /// saved or the points paid.
    /// </summary>
    Task<WithdrawableBalance> GetWithdrawableForUpdateAsync(string userId);

    /// <summary>
    /// The wallets a Google Play refund of <paramref name="points"/> from <paramref name="buyerId"/> and its clawback would
    /// change, read without locks (#27): what <see cref="RefundPlayPurchaseAsync"/> locks.
    /// </summary>
    Task<IReadOnlyList<string>> PlanPlayRefundAsync(string buyerId, decimal points);

    /// <summary>
    /// Takes a voided Google Play purchase back (#22 rule 4, #27), inside the caller's transaction:
    /// <list type="number">
    /// <item>locks <paramref name="wallets"/> and the buyer's in one pass, in the order transfers lock theirs (user id,
    /// ordinal), so a gift or another refund among them waits instead of deadlocking;</item>
    /// <item>reads them again under the locks and takes <paramref name="points"/> from the buyer, even below zero (a
    /// PlayRefund row, <paramref name="purchaseId"/>, <paramref name="description"/>);</item>
    /// <item>takes what the buyer's balance couldn't cover back from the earnings still on hold that the buyer paid for,
    /// newest payment first, whenever they were paid, and down the cascade (<see cref="EarningsClawback"/>): an
    /// EarningReversed row on the author (whose balance may go below zero) and one giving it back to whom paid.</item>
    /// </list>
    /// Throws <see cref="RefundWalletsChangedException"/> before writing anything when the clawback needs a wallet not in
    /// <paramref name="wallets"/>.
    /// </summary>
    Task<PlayRefundOutcome> RefundPlayPurchaseAsync(string buyerId, decimal points, Guid purchaseId, string description,
        IReadOnlyCollection<string> wallets);
}
