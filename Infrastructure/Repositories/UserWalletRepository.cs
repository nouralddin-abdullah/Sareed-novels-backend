using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
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

        // Created on its own connection and committed at once, never inside the caller's transfer transaction: a new
        // wallet row inserted there stays locked until the whole gift or subscription commits, and concurrent first
        // gifts to the same author deadlocked on it (as did the HOLDLOCK range lock this replaced, which also covered
        // the neighbouring UserId key). An empty wallet left behind by a transfer that later fails is harmless.
        // Two first-time requests can both pass NOT EXISTS; the second then hits IX_UserWallets_UserId_Unique.
        var now = DateTime.UtcNow;
        await using var connection = new SqlConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO UserWallets (Id, UserId, CurrentBalance, TotalRecharged, TotalWithdrawn, TotalSpent, TotalEarned, CreatedAt, UpdatedAt)
            SELECT @id, @userId, 0, 0, 0, 0, 0, @now, @now
            WHERE NOT EXISTS (SELECT 1 FROM UserWallets WHERE UserId = @userId)
            """;
        command.Parameters.Add(new SqlParameter("@id", Guid.NewGuid()));
        command.Parameters.Add(new SqlParameter("@userId", System.Data.SqlDbType.NVarChar, 450) { Value = userId });
        command.Parameters.Add(new SqlParameter("@now", System.Data.SqlDbType.DateTime2) { Value = now });
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException ex) when (IsDuplicateKey(ex))
        {
            // Created by a concurrent request.
        }
    }

    private static bool IsDuplicateKey(Exception ex) =>
        (ex as SqlException ?? ex.InnerException as SqlException)?.Number is 2601 or 2627;

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
