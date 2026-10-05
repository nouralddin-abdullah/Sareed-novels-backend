using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Configuration;
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

    public async Task<IReadOnlyList<HeldEarning>> GetHeldEarningsPaidByAsync(string payerId, DateTime now)
    {
        // An earning is held at most the longest hold after it was credited, in the same transaction as its payment: a
        // payment older than that can't have an earning still on hold (this only spares reading older ones).
        var paidSince = now.AddDays(-(WalletSettings.MaxEarningsHoldDays + 1));

        // Both rows of a gift or subscription share RelatedRequestId (the GiftTransaction or subscription), which pairs
        // the payment with the author's earning.
        var rows = await (
                from paid in dbContext.PointTransactions.AsNoTracking()
                where paid.UserId == payerId
                      && (paid.Type == TransactionType.GiftSent || paid.Type == TransactionType.PrivilegeSubscription)
                      && paid.CreatedAt >= paidSince
                      && paid.RelatedRequestId != null
                join earning in dbContext.PointTransactions.AsNoTracking() on paid.RelatedRequestId equals earning.RelatedRequestId
                where earning.UserId != payerId
                      && ((paid.Type == TransactionType.GiftSent && earning.Type == TransactionType.GiftReceived)
                          || (paid.Type == TransactionType.PrivilegeSubscription && earning.Type == TransactionType.PrivilegeRevenue))
                      && earning.AvailableAt > now
                      // A deleted account's balance, held earnings included, was forfeited already.
                      && earning.User.DeletedAt == null
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

    public async Task<IReadOnlyList<EarningsGroup>> GetEarningsGroupsAsync(string userId)
    {
        // One grouped query. A reversal is joined to the earning it took back (by primary key) for that earning's novel;
        // the sign is part of the group so a reversal's two sides are never summed together.
        var groups = await (
                from row in dbContext.PointTransactions.AsNoTracking()
                where row.UserId == userId
                      && (row.Type == TransactionType.GiftReceived || row.Type == TransactionType.PrivilegeRevenue
                          || row.Type == TransactionType.EarningReversed)
                join reversed in dbContext.PointTransactions.AsNoTracking()
                    on row.ReversedTransactionId equals (Guid?)reversed.Id into earnings
                from earning in earnings.DefaultIfEmpty()
                group row.Amount by new
                {
                    NovelId = row.Type == TransactionType.EarningReversed ? earning!.NovelId : row.NovelId,
                    row.CreatedAt.Year,
                    row.CreatedAt.Month,
                    row.Type,
                    Debit = row.Amount < 0
                }
                into g
                select new { g.Key.NovelId, g.Key.Year, g.Key.Month, g.Key.Type, Amount = g.Sum() })
            .ToListAsync();

        return groups.Select(g => new EarningsGroup(g.NovelId, g.Year, g.Month, g.Type, g.Amount)).ToList();
    }

    public async Task<IReadOnlyList<NovelSupporters>> GetSupportersByNovelAsync(string userId)
    {
        // Both rows of a gift or subscription share RelatedRequestId (#22): the earning finds its payment by it, as the
        // refund clawback does the other way round.
        var counts = await (
                from earning in dbContext.PointTransactions.AsNoTracking()
                where earning.UserId == userId
                      && (earning.Type == TransactionType.GiftReceived || earning.Type == TransactionType.PrivilegeRevenue)
                      && earning.RelatedRequestId != null
                join payment in dbContext.PointTransactions.AsNoTracking() on earning.RelatedRequestId equals payment.RelatedRequestId
                where payment.UserId != userId
                      && ((earning.Type == TransactionType.GiftReceived && payment.Type == TransactionType.GiftSent)
                          || (earning.Type == TransactionType.PrivilegeRevenue && payment.Type == TransactionType.PrivilegeSubscription))
                group payment.UserId by earning.NovelId
                into g
                select new { NovelId = g.Key, Count = g.Distinct().Count() })
            .ToListAsync();

        return counts.Select(c => new NovelSupporters(c.NovelId, c.Count)).ToList();
    }
}
