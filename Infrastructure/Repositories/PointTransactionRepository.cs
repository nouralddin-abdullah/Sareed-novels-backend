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

    /// <summary>The user's earning rows (IX_PointTransactions_User_Type_Available covers these queries).</summary>
    private IQueryable<PointTransaction> EarningsOf(string userId) =>
        dbContext.PointTransactions.AsNoTracking()
            .Where(t => t.UserId == userId && (t.Type == TransactionType.GiftReceived || t.Type == TransactionType.PrivilegeRevenue));

    /// <summary>The author's side of reversals: negative EarningReversed rows (the buyer's side is positive).</summary>
    private IQueryable<PointTransaction> ReversalsOf(string userId) =>
        dbContext.PointTransactions.AsNoTracking()
            .Where(r => r.UserId == userId && r.Type == TransactionType.EarningReversed && r.Amount < 0);

    public async Task<EarningsTotals> GetEarningsTotalsAsync(string userId, DateTime now)
    {
        // An earning row always has AvailableAt (a check constraint); one without would count on neither side.
        var gross = await EarningsOf(userId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Released = g.Sum(t => t.AvailableAt <= now ? t.Amount : 0m),
                Held = g.Sum(t => t.AvailableAt > now ? t.Amount : 0m)
            })
            .SingleOrDefaultAsync();
        if (gross is null)
        {
            return new EarningsTotals(0, 0, 0, null);
        }

        // Each reversal goes against the earning it names: while that is held, it lowers what is on hold; once released,
        // it lowers what is released. A reversal whose earning can't be found counts against released, to be safe.
        var reversals = await ReversalsOf(userId)
            .Select(r => new
            {
                Amount = -r.Amount,
                EarningAvailableAt = dbContext.PointTransactions
                    .Where(e => e.Id == r.ReversedTransactionId && e.UserId == userId)
                    .Select(e => e.AvailableAt)
                    .FirstOrDefault()
            })
            .ToListAsync();
        var reversedHeld = reversals.Where(r => r.EarningAvailableAt > now).Sum(r => r.Amount);
        var reversedReleased = reversals.Sum(r => r.Amount) - reversedHeld;

        var held = gross.Held - reversedHeld;
        DateTime? nextReleaseAt = null;
        if (held > 0)
        {
            // The earliest held earning that a reversal hasn't taken back entirely.
            var userReversals = ReversalsOf(userId);
            nextReleaseAt = await EarningsOf(userId)
                .Where(e => e.AvailableAt > now)
                .Where(e => e.Amount > userReversals.Where(r => r.ReversedTransactionId == e.Id).Sum(r => -r.Amount))
                .MinAsync(e => e.AvailableAt);
        }

        return new EarningsTotals(gross.Released, reversedReleased, held,
            nextReleaseAt is { } next ? DateTime.SpecifyKind(next, DateTimeKind.Utc) : null);
    }

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
