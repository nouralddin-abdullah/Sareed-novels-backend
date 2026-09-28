using Domain.Entities;

namespace Application.Services;

/// <summary>What a ledger row was about, beyond its type: the novel, the gift and how many (PointTransaction's columns).</summary>
public sealed record TransactionDetails(Guid? NovelId = null, Guid? GiftId = null, int? GiftCount = null);

public interface IWalletService
{
    Task<UserWallet> GetOrCreateWalletAsync(string userId);
    Task AddPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null, TransactionDetails? details = null);
    Task DeductPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null, TransactionDetails? details = null);
    Task<bool> HasSufficientBalanceAsync(string userId, decimal amount);
    Task SyncUserBalanceAsync(string userId);
    
    /// <summary>
    /// Atomically transfers points from one user to another within a transaction.
    /// Must be called within an existing transaction scope.
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
}
