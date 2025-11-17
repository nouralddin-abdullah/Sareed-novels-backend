using Domain.Entities;

namespace Application.Services;

public interface IWalletService
{
    Task<UserWallet> GetOrCreateWalletAsync(string userId);
    Task AddPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null);
    Task DeductPointsAsync(string userId, decimal amount, string transactionType, string description, Guid? relatedRequestId = null);
    Task<bool> HasSufficientBalanceAsync(string userId, decimal amount);
    Task SyncUserBalanceAsync(string userId);
}
