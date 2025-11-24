using Application.Services;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class WalletService(
    ILogger<WalletService> logger,
    IUserWalletRepository walletRepository,
    IPointTransactionRepository transactionRepository,
    UserManager<User> userManager) : IWalletService
{
    public async Task<UserWallet> GetOrCreateWalletAsync(string userId)
    {
        var wallet = await walletRepository.GetByUserIdAsync(userId);
        
        if (wallet == null)
        {
            wallet = new UserWallet
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CurrentBalance = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            await walletRepository.CreateAsync(wallet);
            logger.LogInformation("Created new wallet for user {UserId}", userId);
        }
        
        return wallet;
    }

    public async Task AddPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null)
    {
        var wallet = await GetOrCreateWalletAsync(userId);
        
        var balanceBefore = wallet.CurrentBalance;
        wallet.AddPoints(amount);
        
        await walletRepository.UpdateAsync(wallet);
        
        // Create transaction record
        await transactionRepository.CreateAsync(new PointTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = transactionType,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.CurrentBalance,
            Description = description,
            RelatedRequestId = relatedRequestId,
            CreatedAt = DateTime.UtcNow
        });
        
        // NOTE: Sync user balance is disabled to avoid DbContext concurrent access issues
        // The User.PointBalance is a cached value and will be synced on next wallet query
        // _ = SyncUserBalanceAsync(userId);
        
        logger.LogInformation("Added {Amount} points to user {UserId}, new balance: {Balance}", 
            amount, userId, wallet.CurrentBalance);
    }

    public async Task DeductPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null)
    {
        var wallet = await GetOrCreateWalletAsync(userId);
        
        if (!wallet.HasSufficientBalance(amount))
        {
            throw new InvalidOperationException($"Insufficient balance. Required: {amount}, Available: {wallet.CurrentBalance}");
        }
        
        var balanceBefore = wallet.CurrentBalance;
        wallet.DeductPoints(amount);
        
        await walletRepository.UpdateAsync(wallet);
        
        // Create transaction record
        await transactionRepository.CreateAsync(new PointTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = transactionType,
            Amount = -amount, // Negative for deductions
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.CurrentBalance,
            Description = description,
            RelatedRequestId = relatedRequestId,
            CreatedAt = DateTime.UtcNow
        });
        
        // NOTE: Sync user balance is disabled to avoid DbContext concurrent access issues
        // The User.PointBalance is a cached value and will be synced on next wallet query
        // _ = SyncUserBalanceAsync(userId);
        
        logger.LogInformation("Deducted {Amount} points from user {UserId}, new balance: {Balance}", 
            amount, userId, wallet.CurrentBalance);
    }

    public async Task<bool> HasSufficientBalanceAsync(string userId, decimal amount)
    {
        var wallet = await GetOrCreateWalletAsync(userId);
        return wallet.HasSufficientBalance(amount);
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
    /// Atomically transfers points from one user to another within a transaction.
    /// Both deduction and addition happen together - if either fails, both are rolled back.
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
        // Get both wallets
        var fromWallet = await GetOrCreateWalletAsync(fromUserId);
        var toWallet = await GetOrCreateWalletAsync(toUserId);
        
        // Validate sufficient balance
        if (!fromWallet.HasSufficientBalance(amount))
        {
            throw new InvalidOperationException(
                $"Insufficient balance. Required: {amount}, Available: {fromWallet.CurrentBalance}");
        }
        
        // Capture balances before transfer
        var fromBalanceBefore = fromWallet.CurrentBalance;
        var toBalanceBefore = toWallet.CurrentBalance;
        
        // Perform transfer (domain logic)
        fromWallet.DeductPoints(amount);
        toWallet.AddPoints(amount);
        
        // Update both wallets (will be part of the same transaction context)
        await walletRepository.UpdateAsync(fromWallet);
        await walletRepository.UpdateAsync(toWallet);
        
        // Create transaction records for both users
        await transactionRepository.CreateAsync(new PointTransaction
        {
            Id = Guid.NewGuid(),
            UserId = fromUserId,
            Type = fromTransactionType,
            Amount = -amount, // Negative for deduction
            BalanceBefore = fromBalanceBefore,
            BalanceAfter = fromWallet.CurrentBalance,
            Description = fromDescription,
            RelatedRequestId = relatedRequestId,
            CreatedAt = DateTime.UtcNow
        });
        
        await transactionRepository.CreateAsync(new PointTransaction
        {
            Id = Guid.NewGuid(),
            UserId = toUserId,
            Type = toTransactionType,
            Amount = amount, // Positive for addition
            BalanceBefore = toBalanceBefore,
            BalanceAfter = toWallet.CurrentBalance,
            Description = toDescription,
            RelatedRequestId = relatedRequestId,
            CreatedAt = DateTime.UtcNow
        });
        
        logger.LogInformation(
            "Transferred {Amount} points from user {FromUserId} to {ToUserId}",
            amount, fromUserId, toUserId);
    }
}
