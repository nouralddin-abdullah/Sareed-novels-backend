using Domain.Entities;

namespace Domain.Repositories;

public interface IUserWalletRepository
{
    Task<UserWallet?> GetByUserIdAsync(string userId);

    /// <summary>The balance as the database holds it now (0 without a wallet), never a tracked copy.</summary>
    Task<decimal> GetBalanceAsync(string userId);

    /// <summary>
    /// <see cref="GetBalanceAsync"/> with an update lock on the wallet row, held until the caller's transaction ends (call
    /// inside one): every other lock, credit or debit of this wallet waits until then. Locks nothing without a wallet.
    /// </summary>
    Task<decimal> LockBalanceAsync(string userId);

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

    /// <summary>Atomically subtracts <paramref name="amount"/> even if that takes the balance below zero, and returns the
    /// new balance. Only for taking back points that were never paid for in the end (a voided Google Play purchase);
    /// spending goes through <see cref="TryDebitAsync"/>, which a negative balance always refuses. The wallet must exist.
    /// Call inside a transaction so the returned balance is the one this change produced.</summary>
    Task<decimal> DebitAllowingNegativeAsync(string userId, decimal amount);
}
