using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Every balance change is a single conditional SQL UPDATE (never read-modify-write) and is committed together with its
/// PointTransaction row: each method joins the caller's transaction or opens its own.
/// </summary>
public class WalletService(
    ILogger<WalletService> logger,
    IUserWalletRepository walletRepository,
    IPointTransactionRepository transactionRepository,
    UserManager<User> userManager,
    ITransactionManager transactionManager) : IWalletService
{
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

    public async Task AddPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null)
    {
        EnsurePositive(amount);

        var balanceAfter = await transactionManager.InTransactionAsync(async () =>
        {
            await walletRepository.EnsureExistsAsync(userId);
            var after = await walletRepository.CreditAsync(userId, amount);
            await RecordAsync(userId, amount, after, transactionType, description, relatedRequestId);
            return after;
        });

        logger.LogInformation("Added {Amount} points to user {UserId}, new balance: {Balance}",
            amount, userId, balanceAfter);
    }

    public async Task DeductPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null)
    {
        EnsurePositive(amount);

        var balanceAfter = await transactionManager.InTransactionAsync(async () =>
        {
            await walletRepository.EnsureExistsAsync(userId);
            var after = await DebitOrThrowAsync(userId, amount);
            await RecordAsync(userId, -amount, after, transactionType, description, relatedRequestId);
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
        Guid? relatedRequestId = null)
    {
        EnsurePositive(amount);
        if (fromUserId == toUserId)
        {
            throw new ArgumentException("Cannot transfer points to the same user");
        }

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

            await RecordAsync(fromUserId, -amount, fromAfter, fromTransactionType, fromDescription, relatedRequestId);
            await RecordAsync(toUserId, amount, toAfter, toTransactionType, toDescription, relatedRequestId);
        });

        logger.LogInformation(
            "Transferred {Amount} points from user {FromUserId} to {ToUserId}",
            amount, fromUserId, toUserId);
    }

    private async Task<decimal> DebitOrThrowAsync(string userId, decimal amount) =>
        await walletRepository.TryDebitAsync(userId, amount)
        ?? throw new InsufficientBalanceException(amount);

    private Task RecordAsync(string userId, decimal signedAmount, decimal balanceAfter, string type, string description, Guid? relatedRequestId) =>
        transactionRepository.CreateAsync(new PointTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Amount = signedAmount,
            BalanceBefore = balanceAfter - signedAmount,
            BalanceAfter = balanceAfter,
            Description = description,
            RelatedRequestId = relatedRequestId,
            CreatedAt = DateTime.UtcNow
        });

    private static void EnsurePositive(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive");
        }
    }
}
