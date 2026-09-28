using Application.Services;
using Application.Wallet;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Infrastructure.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// Every balance change is a single conditional SQL UPDATE (never read-modify-write) and is committed together with its
/// PointTransaction row: each method joins the caller's transaction or opens its own. Earnings are held for
/// Wallet:EarningsHoldDays before they can be withdrawn, and only earnings can be (#22).
/// </summary>
public class WalletService(
    ILogger<WalletService> logger,
    IUserWalletRepository walletRepository,
    IPointTransactionRepository transactionRepository,
    IWithdrawalRequestRepository withdrawalRepository,
    UserManager<User> userManager,
    ITransactionManager transactionManager,
    TimeProvider clock,
    IOptions<WalletSettings> settings) : IWalletService
{
    private int HoldDays => settings.Value.EarningsHoldDays;

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    public async Task<UserWallet> GetOrCreateWalletAsync(string userId)
    {
        var wallet = await walletRepository.GetByUserIdAsync(userId);
        if (wallet != null)
        {
            return wallet;
        }

        await walletRepository.EnsureExistsAsync(userId);
        logger.LogInformation("Created new wallet for user {UserId}", userId);
        return (await walletRepository.GetByUserIdAsync(userId))!;
    }

    public async Task AddPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null, TransactionDetails? details = null)
    {
        EnsurePositive(amount);

        var balanceAfter = await transactionManager.InTransactionAsync(async () =>
        {
            await walletRepository.EnsureExistsAsync(userId);
            var after = await walletRepository.CreditAsync(userId, amount);
            await RecordAsync(userId, amount, after, transactionType, description, relatedRequestId, details);
            return after;
        });

        logger.LogInformation("Added {Amount} points to user {UserId}, new balance: {Balance}",
            amount, userId, balanceAfter);
    }

    public async Task DeductPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null, TransactionDetails? details = null)
    {
        EnsurePositive(amount);

        var balanceAfter = await transactionManager.InTransactionAsync(async () =>
        {
            await walletRepository.EnsureExistsAsync(userId);
            var after = await DebitOrThrowAsync(userId, amount);
            await RecordAsync(userId, -amount, after, transactionType, description, relatedRequestId, details);
            return after;
        });

        logger.LogInformation("Deducted {Amount} points from user {UserId}, new balance: {Balance}",
            amount, userId, balanceAfter);
    }

    public async Task<bool> HasSufficientBalanceAsync(string userId, decimal amount)
    {
        // A pre-check for friendly messages only; the debit itself re-checks atomically.
        var wallet = await walletRepository.GetByUserIdAsync(userId);
        return (wallet?.CurrentBalance ?? 0) >= amount;
    }

    public async Task SyncUserBalanceAsync(string userId)
    {
        try
        {
            var wallet = await walletRepository.GetByUserIdAsync(userId);
            if (wallet == null) return;

            var user = await userManager.FindByIdAsync(userId);
            if (user == null) return;

            user.PointBalance = wallet.CurrentBalance;
            user.PointBalanceLastUpdated = DateTime.UtcNow;

            await userManager.UpdateAsync(user);

            logger.LogDebug("Synced cached balance for user {UserId}: {Balance}", userId, wallet.CurrentBalance);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync cached balance for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Moves points from one user to another: both balance changes and both ledger rows commit together or not at all.
    /// Throws <see cref="InvalidOperationException"/> if the sender's balance doesn't cover <paramref name="amount"/>.
    /// </summary>
    public async Task TransferPointsAsync(
        string fromUserId,
        string toUserId,
        decimal amount,
        string fromTransactionType,
        string toTransactionType,
        string fromDescription,
        string toDescription,
        Guid? relatedRequestId = null,
        TransactionDetails? details = null)
    {
        EnsurePositive(amount);
        if (fromUserId == toUserId)
        {
            throw new ArgumentException("Cannot transfer points to the same user");
        }

        // The two rows always share an id: the refund clawback finds the author's earning from the reader's payment by it.
        relatedRequestId ??= Guid.NewGuid();

        await transactionManager.InTransactionAsync(async () =>
        {
            // Touch the two wallets in a fixed order (by user id) so opposite transfers (A->B while B->A) take their
            // row locks in the same order and can't deadlock.
            var senderFirst = string.CompareOrdinal(fromUserId, toUserId) < 0;
            await walletRepository.EnsureExistsAsync(senderFirst ? fromUserId : toUserId);
            await walletRepository.EnsureExistsAsync(senderFirst ? toUserId : fromUserId);

            decimal fromAfter, toAfter;
            if (senderFirst)
            {
                fromAfter = await DebitOrThrowAsync(fromUserId, amount);
                toAfter = await walletRepository.CreditAsync(toUserId, amount);
            }
            else
            {
                toAfter = await walletRepository.CreditAsync(toUserId, amount);
                fromAfter = await DebitOrThrowAsync(fromUserId, amount); // throwing rolls the credit back
            }

            await RecordAsync(fromUserId, -amount, fromAfter, fromTransactionType, fromDescription, relatedRequestId, details);
            await RecordAsync(toUserId, amount, toAfter, toTransactionType, toDescription, relatedRequestId, details);
        });

        logger.LogInformation(
            "Transferred {Amount} points from user {FromUserId} to {ToUserId}",
            amount, fromUserId, toUserId);
    }

    public async Task<WithdrawableBalance> GetWithdrawableAsync(string userId)
    {
        // The ledger before the balance: a change committed in between is in the balance only, so it counts as if it came
        // before the ledger (a credit as bought points, a debit as a debt the first credits pay), which can only lower
        // what this shows. Deciding goes through GetWithdrawableForUpdateAsync.
        var now = Now();
        var ledger = await transactionRepository.GetLedgerAsync(userId);
        var balance = await walletRepository.GetBalanceAsync(userId);
        return await WithdrawableAsync(userId, balance, ledger, now);
    }

    public async Task<WithdrawableBalance> GetWithdrawableForUpdateAsync(string userId)
    {
        // Every change to a user's ledger comes with a change to their wallet row, which waits for this lock: the balance
        // and the ledger read under it agree.
        var balance = await walletRepository.LockBalanceAsync(userId);
        var now = Now();
        return await WithdrawableAsync(userId, balance, await transactionRepository.GetLedgerAsync(userId), now);
    }

    private async Task<WithdrawableBalance> WithdrawableAsync(string userId, decimal balance, IReadOnlyList<LedgerEntry> ledger, DateTime now)
    {
        var pools = WalletPools.Fold(balance, ledger, now);
        var pendingWithdrawals = await withdrawalRepository.GetPendingPointsAsync(userId);
        return WithdrawableBalance.From(balance, pools, pendingWithdrawals, HoldDays, now);
    }

    /// <summary>
    /// A refund takes back earnings held beyond this margin after it: those about to be released are left alone, so each
    /// one it takes back is still held at every row it writes (consecutive instants from its start, see
    /// <see cref="RefundPlayPurchaseAsync"/>), and the pools read them back as it meant them.
    /// </summary>
    internal static readonly TimeSpan HeldMargin = TimeSpan.FromSeconds(1);

    public async Task<IReadOnlyList<string>> PlanPlayRefundAsync(string buyerId, decimal points)
    {
        EnsurePositive(points);
        var heldAfter = Now() + HeldMargin;
        var plan = await EarningsClawback.PlanAsync(buyerId, points,
            async userId => await walletRepository.GetBalanceAsync(userId),
            payer => transactionRepository.GetHeldEarningsPaidByAsync(payer, heldAfter));
        return plan.Wallets;
    }

    public async Task<PlayRefundOutcome> RefundPlayPurchaseAsync(string buyerId, decimal points, Guid purchaseId, string description,
        IReadOnlyCollection<string> wallets)
    {
        EnsurePositive(points);

        return await transactionManager.InTransactionAsync(async () =>
        {
            // Every wallet the refund may change, locked in one pass in the order transfers lock theirs (user id,
            // ordinal): a gift or a second refund touching any of them waits for this one instead of deadlocking with it.
            var locked = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var userId in wallets.Append(buyerId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                locked[userId] = await walletRepository.LockBalanceAsync(userId);
            }

            // Read again under the locks: until the commit nothing changes these wallets or their ledgers. A wallet the
            // plan needs and doesn't hold means the data changed since the plan: nothing is written, the caller starts again.
            var now = Now();
            var plan = await EarningsClawback.PlanAsync(buyerId, points,
                userId => Task.FromResult(locked.TryGetValue(userId, out var balance) ? balance : (decimal?)null),
                payer => transactionRepository.GetHeldEarningsPaidByAsync(payer, now + HeldMargin));
            if (plan.Missing.Count > 0)
            {
                throw new RefundWalletsChangedException(plan.Missing);
            }

            // The refund's rows get consecutive instants from now on, in the order written: the ledger reads back in that
            // order, whatever the clock does meanwhile.
            var at = now;
            DateTime Next()
            {
                var stamp = at;
                at = at.AddTicks(1);
                return stamp;
            }

            var buyerAfter = await walletRepository.DebitAllowingNegativeAsync(buyerId, points);
            await RecordAsync(buyerId, -points, buyerAfter, TransactionType.PlayRefund, description, purchaseId, null, at: Next());

            foreach (var reversal in plan.Reversals)
            {
                var (earning, payerId, amount) = (reversal.Earning, reversal.PayerId, reversal.Amount);
                var details = new TransactionDetails(earning.NovelId, earning.GiftId, earning.GiftCount);

                // Below zero if the author already spent it: a negative balance refuses every kind of spending.
                var authorAfter = await walletRepository.DebitAllowingNegativeAsync(earning.AuthorId, amount);
                await RecordAsync(earning.AuthorId, -amount, authorAfter, TransactionType.EarningReversed,
                    TransactionDescriptions.EarningReversed(amount), purchaseId, details, earning.EarningId, Next());

                var payerAfter = await walletRepository.CreditAsync(payerId, amount);
                await RecordAsync(payerId, amount, payerAfter, TransactionType.EarningReversed,
                    TransactionDescriptions.EarningReversalReturned(amount), purchaseId, details, earning.EarningId, Next());

                logger.LogWarning(
                    "Took back {Amount} points of earning {EarningId} from author {AuthorId} (balance now {AuthorBalance}): purchase {PurchaseId} that paid for it was refunded; returned to {PayerId} (balance now {PayerBalance}){Cascade}",
                    amount, earning.EarningId, earning.AuthorId, authorAfter, purchaseId, payerId, payerAfter,
                    reversal.Depth == 0 ? "" : $", {reversal.Depth} account(s) past the buyer {buyerId}");
            }

            // Under the locks the writes can only do what the plan said.
            foreach (var (userId, expected) in plan.BalancesAfter)
            {
                var actual = await walletRepository.GetBalanceAsync(userId);
                if (actual != expected)
                {
                    logger.LogError("Refund of purchase {PurchaseId}: wallet of {UserId} ended at {Actual}, the plan said {Expected}",
                        purchaseId, userId, actual, expected);
                }
            }

            if (plan.Uncovered > 0)
            {
                logger.LogWarning(
                    "Refund of purchase {PurchaseId} left buyer {BuyerId} {Uncovered} points short that no earning still on hold could cover",
                    purchaseId, buyerId, plan.Uncovered);
            }

            return new PlayRefundOutcome(plan.BalancesAfter[buyerId], plan.ReturnedToBuyer, plan.Reversals.Sum(r => r.Amount),
                plan.Reversals.Count, plan.Uncovered);
        });
    }

    private async Task<decimal> DebitOrThrowAsync(string userId, decimal amount) =>
        await walletRepository.TryDebitAsync(userId, amount)
        ?? throw new InsufficientBalanceException(amount);

    private Task RecordAsync(string userId, decimal signedAmount, decimal balanceAfter, string type, string description, Guid? relatedRequestId,
        TransactionDetails? details, Guid? reversedTransactionId = null, DateTime? at = null)
    {
        var now = at ?? Now();
        return transactionRepository.CreateAsync(new PointTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Amount = signedAmount,
            BalanceBefore = balanceAfter - signedAmount,
            BalanceAfter = balanceAfter,
            Description = description,
            RelatedRequestId = relatedRequestId,
            NovelId = details?.NovelId,
            GiftId = details?.GiftId,
            GiftCount = details?.GiftCount,
            // Earnings are held; everything else has no hold.
            AvailableAt = TransactionType.IsEarning(type) ? now.AddDays(HoldDays) : null,
            ReversedTransactionId = reversedTransactionId,
            CreatedAt = now
        });
    }

    private static void EnsurePositive(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive");
        }
    }
}
