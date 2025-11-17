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
}
