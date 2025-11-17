using Domain.Entities;

namespace Domain.Repositories;

public interface IUserWalletRepository
{
    Task<UserWallet?> GetByUserIdAsync(string userId);
    Task<UserWallet> CreateAsync(UserWallet wallet);
    Task<bool> UpdateAsync(UserWallet wallet);
}
