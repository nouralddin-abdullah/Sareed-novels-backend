using Domain.Entities;

namespace Application.Services;

/// <summary>What a ledger row was about, beyond its type: the novel, the gift and how many (PointTransaction's columns).</summary>
public sealed record TransactionDetails(Guid? NovelId = null, Guid? GiftId = null, int? GiftCount = null);

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
}
