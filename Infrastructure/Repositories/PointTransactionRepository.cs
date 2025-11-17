using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class PointTransactionRepository(ApplicationDbContext dbContext) : IPointTransactionRepository
{
    public async Task<PointTransaction> CreateAsync(PointTransaction transaction)
    {
        dbContext.PointTransactions.Add(transaction);
        await dbContext.SaveChangesAsync();
        return transaction;
    }

    public async Task<(IEnumerable<PointTransaction>, int)> GetUserTransactionsAsync(string userId, int pageNumber, int pageSize)
    {
        var query = dbContext.PointTransactions
            .Where(t => t.UserId == userId);

        var totalCount = await query.CountAsync();

        var transactions = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (transactions, totalCount);
    }
}
