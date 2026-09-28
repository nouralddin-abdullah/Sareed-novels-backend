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

    public async Task<decimal> ReverseHeldEarningsAsync(string buyerId, Guid voidedPurchaseId, DateTime paidSince, decimal deficit)
    {
        if (deficit <= 0)
        {
            return 0;
        }

        return await transactionManager.InTransactionAsync(async () =>
        {
            // The buyer's wallet stays locked until the commit (the caller's refund locked it first): a second refund of
            // theirs waits for this one and then sees what it reversed, so no earning is taken back twice.
            await walletRepository.LockBalanceAsync(buyerId);
            var held = await transactionRepository.GetHeldEarningsPaidByAsync(buyerId, paidSince, Now());
            var reversals = EarningsClawback.Allocate(deficit, held);

            // Each author's wallet is locked before it changes (it may change more than once here), in a fixed order so
            // that clawbacks sharing authors take their locks the same way round.
            foreach (var authorId in reversals.Select(r => r.Earning.AuthorId).Distinct().Order(StringComparer.Ordinal))
            {
                await walletRepository.LockBalanceAsync(authorId);
            }

            var reversed = 0m;
            foreach (var (earning, amount) in reversals)
            {
                var details = new TransactionDetails(earning.NovelId, earning.GiftId, earning.GiftCount);

                // Below zero if the author already spent it: a negative balance refuses every kind of spending.
                var authorAfter = await walletRepository.DebitAllowingNegativeAsync(earning.AuthorId, amount);
                await RecordAsync(earning.AuthorId, -amount, authorAfter, TransactionType.EarningReversed,
                    TransactionDescriptions.EarningReversed(amount), voidedPurchaseId, details, earning.EarningId);

                var buyerAfter = await walletRepository.CreditAsync(buyerId, amount);
                await RecordAsync(buyerId, amount, buyerAfter, TransactionType.EarningReversed,
                    TransactionDescriptions.EarningReversalReturned(amount), voidedPurchaseId, details, earning.EarningId);

                reversed += amount;
                logger.LogWarning(
                    "Took back {Amount} points of earning {EarningId} from author {AuthorId} (balance now {AuthorBalance}): purchase {PurchaseId} that paid for it was refunded; returned to buyer {BuyerId} (balance now {BuyerBalance})",
                    amount, earning.EarningId, earning.AuthorId, authorAfter, voidedPurchaseId, buyerId, buyerAfter);
            }

            if (reversed < deficit)
            {
                logger.LogWarning(
                    "Refund of purchase {PurchaseId} left buyer {BuyerId} {Uncovered} points short that no earning still on hold could cover",
                    voidedPurchaseId, buyerId, deficit - reversed);
            }
            return reversed;
        });
    }

    private async Task<decimal> DebitOrThrowAsync(string userId, decimal amount) =>
        await walletRepository.TryDebitAsync(userId, amount)
        ?? throw new InsufficientBalanceException(amount);

    private Task RecordAsync(string userId, decimal signedAmount, decimal balanceAfter, string type, string description, Guid? relatedRequestId,
        TransactionDetails? details, Guid? reversedTransactionId = null)
    {
        var now = Now();
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
