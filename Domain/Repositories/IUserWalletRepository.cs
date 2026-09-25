using Domain.Entities;

namespace Domain.Repositories;

public interface IUserWalletRepository
{
    Task<UserWallet?> GetByUserIdAsync(string userId);
    Task<UserWallet> CreateAsync(UserWallet wallet);
    Task<bool> UpdateAsync(UserWallet wallet);

    /// <summary>Creates an empty wallet for the user unless one exists; safe when called concurrently.</summary>
    Task EnsureExistsAsync(string userId);

    /// <summary>Atomically adds <paramref name="amount"/> and returns the new balance. The wallet must exist.
    /// Call inside a transaction so the returned balance is the one this change produced.</summary>
    Task<decimal> CreditAsync(string userId, decimal amount);

    /// <summary>Atomically subtracts <paramref name="amount"/> only if the balance covers it (one conditional UPDATE,
    /// so concurrent debits can't overdraw). Returns the new balance, or null if the balance was too low.
    /// Call inside a transaction so the returned balance is the one this change produced.</summary>
    Task<decimal?> TryDebitAsync(string userId, decimal amount);
}
