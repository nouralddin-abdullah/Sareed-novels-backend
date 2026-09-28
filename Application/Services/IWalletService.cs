using Domain.Entities;

namespace Application.Services;

/// <summary>What a ledger row was about, beyond its type: the novel, the gift and how many (PointTransaction's columns).</summary>
public sealed record TransactionDetails(Guid? NovelId = null, Guid? GiftId = null, int? GiftCount = null);

/// <summary>
/// What a user can withdraw (#22). Only earnings (gifts and privilege subscriptions received) are ever paid out, once
/// their hold has ended; bought points (website top-ups, Google Play) are spent inside Sard only.
/// </summary>
/// <param name="Balance">The wallet balance (may be negative after a refund).</param>
/// <param name="ReleasedEarnings">Earnings whose hold has ended.</param>
/// <param name="ReversedEarnings">What refunds took back of those released earnings (reversals of earnings still on hold
/// are netted out of <paramref name="PendingEarnings"/> instead, so none is counted twice).</param>
/// <param name="Withdrawn">Points of approved withdrawals.</param>
/// <param name="PendingWithdrawals">Points of withdrawals requested and not decided yet: reserved, though not deducted
/// from the balance until approval.</param>
/// <param name="PendingEarnings">Earnings still on hold, less what refunds took back of them.</param>
/// <param name="NextReleaseAt">When the next of those becomes withdrawable (UTC); null when none is on hold.</param>
/// <param name="HoldDays">The hold (Wallet:EarningsHoldDays).</param>
/// <param name="AsOf">The moment this was computed (UTC).</param>
public sealed record WithdrawableBalance(
    decimal Balance,
    decimal ReleasedEarnings,
    decimal ReversedEarnings,
    decimal Withdrawn,
    decimal PendingWithdrawals,
    decimal PendingEarnings,
    DateTime? NextReleaseAt,
    int HoldDays,
    DateTime AsOf)
{
    /// <summary>
    /// What approving a withdrawal can pay out now: min(balance, released − withdrawn − reversed), never below 0. The
    /// min means points spent come out of bought points first and only then out of earnings. Pending requests don't
    /// count here: an approval pays one request against what is actually left, and the next one is checked again.
    /// </summary>
    public decimal Payable => Math.Max(0, Math.Min(Balance, ReleasedEarnings - Withdrawn - ReversedEarnings));

    /// <summary>
    /// What a new request can ask for (#22 rule 3): <see cref="Payable"/> less the pending requests, never below 0. They
    /// come off both sides of the min, since they haven't left the balance yet: two requests can't use the same points.
    /// </summary>
    public decimal Withdrawable => Math.Max(0, Payable - PendingWithdrawals);
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

    /// <summary>What the user can withdraw now, and their earnings still on hold (#22).</summary>
    Task<WithdrawableBalance> GetWithdrawableAsync(string userId);

    /// <summary>
    /// <see cref="GetWithdrawableAsync"/> under an update lock on the user's wallet row, held until the caller's
    /// transaction ends (call inside one). Withdrawal requests and approvals for the user, debits and clawbacks of their
    /// balance then take turns, so what is checked is still true when the request is saved or the points paid.
    /// </summary>
    Task<WithdrawableBalance> GetWithdrawableForUpdateAsync(string userId);
}
