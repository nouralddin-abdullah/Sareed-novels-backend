using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class UserWalletRepository(ApplicationDbContext dbContext) : IUserWalletRepository
{
    public async Task<UserWallet?> GetByUserIdAsync(string userId)
    {
        return await dbContext.UserWallets
            .FirstOrDefaultAsync(w => w.UserId == userId);
    }

    public async Task<UserWallet> CreateAsync(UserWallet wallet)
    {
        dbContext.UserWallets.Add(wallet);
        await dbContext.SaveChangesAsync();
        return wallet;
    }

    public async Task<bool> UpdateAsync(UserWallet wallet)
    {
        wallet.UpdatedAt = DateTime.UtcNow;
        dbContext.UserWallets.Update(wallet);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task EnsureExistsAsync(string userId)
    {
        if (await dbContext.UserWallets.AnyAsync(w => w.UserId == userId))
        {
            return;
        }

        // UPDLOCK+HOLDLOCK makes the existence check and the insert one step, so two first-time requests for the same
        // user can't both insert (the second used to fail on IX_UserWallets_UserId_Unique).
        var now = DateTime.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO UserWallets (Id, UserId, CurrentBalance, TotalRecharged, TotalWithdrawn, TotalSpent, TotalEarned, CreatedAt, UpdatedAt)
            SELECT {Guid.NewGuid()}, {userId}, 0, 0, 0, 0, 0, {now}, {now}
            WHERE NOT EXISTS (SELECT 1 FROM UserWallets WITH (UPDLOCK, HOLDLOCK) WHERE UserId = {userId})
            """);
    }

    public async Task<decimal> CreditAsync(string userId, decimal amount)
    {
        var updated = await dbContext.UserWallets
            .Where(w => w.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.CurrentBalance, w => w.CurrentBalance + amount)
                .SetProperty(w => w.UpdatedAt, DateTime.UtcNow));

        if (updated == 0)
        {
            throw new InvalidOperationException($"No wallet for user {userId}");
        }

        return await CurrentBalanceAsync(userId);
    }

    public async Task<decimal?> TryDebitAsync(string userId, decimal amount)
    {
        var updated = await dbContext.UserWallets
            .Where(w => w.UserId == userId && w.CurrentBalance >= amount)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.CurrentBalance, w => w.CurrentBalance - amount)
                .SetProperty(w => w.UpdatedAt, DateTime.UtcNow));

        return updated == 0 ? null : await CurrentBalanceAsync(userId);
    }

    // Inside the caller's transaction our UPDATE still holds the row lock, so this reads the balance we produced.
    private Task<decimal> CurrentBalanceAsync(string userId) =>
        dbContext.UserWallets.AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => w.CurrentBalance)
            .SingleAsync();
}
