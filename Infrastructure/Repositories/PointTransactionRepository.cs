using Domain.Constants;
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

    public async Task<IReadOnlyList<LedgerEntry>> GetLedgerAsync(string userId) =>
        await dbContext.PointTransactions.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new LedgerEntry(t.Id, t.Type, t.Amount, t.BalanceBefore, t.BalanceAfter, t.CreatedAt, t.AvailableAt,
                t.ReversedTransactionId))
            .ToListAsync();

    public async Task<IReadOnlyList<HeldEarning>> GetHeldEarningsPaidByAsync(string buyerId, DateTime paidSince, DateTime now)
    {
        // Both rows of a gift or subscription share RelatedRequestId (the GiftTransaction or subscription), which pairs
        // the reader's payment with the author's earning.
        var rows = await (
                from paid in dbContext.PointTransactions.AsNoTracking()
                where paid.UserId == buyerId
                      && (paid.Type == TransactionType.GiftSent || paid.Type == TransactionType.PrivilegeSubscription)
                      && paid.CreatedAt >= paidSince
                      && paid.RelatedRequestId != null
                join earning in dbContext.PointTransactions.AsNoTracking() on paid.RelatedRequestId equals earning.RelatedRequestId
                where earning.UserId != buyerId
                      && ((paid.Type == TransactionType.GiftSent && earning.Type == TransactionType.GiftReceived)
                          || (paid.Type == TransactionType.PrivilegeSubscription && earning.Type == TransactionType.PrivilegeRevenue))
                      && earning.AvailableAt > now
                orderby paid.CreatedAt descending, paid.Id descending
                select new
                {
                    earning.Id,
                    earning.UserId,
                    earning.Amount,
                    earning.NovelId,
                    earning.GiftId,
                    earning.GiftCount,
                    PaidAt = paid.CreatedAt,
                    Reversed = dbContext.PointTransactions
                        .Where(r => r.ReversedTransactionId == earning.Id && r.UserId == earning.UserId
                                    && r.Type == TransactionType.EarningReversed && r.Amount < 0)
                        .Sum(r => -r.Amount)
                })
            .ToListAsync();

        return rows
            .Select(r => new HeldEarning(r.Id, r.UserId, r.Amount - r.Reversed, r.NovelId, r.GiftId, r.GiftCount, r.PaidAt))
            .ToList();
    }

    public async Task<IReadOnlyList<PointTransaction>> GetEarningReversalsAsync(IReadOnlyCollection<string> userIds, DateTime since, int perUser)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var ids = userIds.Distinct().ToList();
        var rows = await dbContext.PointTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.UserId) && t.Type == TransactionType.EarningReversed && t.CreatedAt >= since)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Reversals are rare: trimming each user's list here is cheaper than a window function.
        return rows.GroupBy(t => t.UserId).SelectMany(g => g.Take(perUser)).ToList();
    }
}
