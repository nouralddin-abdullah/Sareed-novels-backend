using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class GiftTransactionRepository(ApplicationDbContext dbContext) : IGiftTransactionRepository
{
    public async Task<GiftTransaction> CreateTransaction(GiftTransaction transaction)
    {
        dbContext.GiftTransactions.Add(transaction);
        await dbContext.SaveChangesAsync();
        return transaction;
    }

    public async Task<(IEnumerable<GiftTransaction> transactions, int totalCount)> GetTransactionsByNovel(Guid novelId, int pageNumber, int pageSize)
    {
        var query = dbContext.GiftTransactions
            .Where(t => t.NovelId == novelId)
            .Include(t => t.Gift)
            .Include(t => t.Sender)
            .Include(t => t.Novel)
            .OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync();

        var transactions = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (transactions, totalCount);
    }

    public async Task<(IEnumerable<GiftTransaction> transactions, int totalCount)> GetTransactionsBySender(string senderId, int pageNumber, int pageSize)
    {
        // Gifts to a deleted novel are left out of the count as well as the page: the Include's join (Novel has a
        // query filter) dropped them from the page, but the count still counted them.
        var query = dbContext.GiftTransactions
            .Where(t => t.SenderId == senderId && !t.Novel.IsDeleted)
            .Include(t => t.Gift)
            .Include(t => t.Novel)
            .OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync();

        var transactions = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (transactions, totalCount);
    }

    public async Task<List<(string UserId, decimal TotalPoints, int TotalGifts)>> GetTopSupportersForNovel(Guid novelId, int topCount)
    {
        // Real-time aggregation for per-novel top supporters; deleted accounts aren't listed (their gifts still count
        // towards the novel's totals).
        return await dbContext.GiftTransactions
            .Where(t => t.NovelId == novelId && t.Sender.DeletedAt == null)
            .GroupBy(t => t.SenderId)
            .Select(g => new
            {
                UserId = g.Key,
                TotalPoints = g.Sum(t => t.TotalCost),
                TotalGifts = g.Sum(t => t.Count)
            })
            .OrderByDescending(x => x.TotalPoints)
            .Take(topCount)
            .Select(x => ValueTuple.Create(x.UserId, x.TotalPoints, x.TotalGifts))
            .ToListAsync();
    }

    public async Task<decimal> GetTotalPointsReceivedByNovel(Guid novelId)
    {
        return await dbContext.GiftTransactions
            .Where(t => t.NovelId == novelId)
            .SumAsync(t => t.TotalCost);
    }

    public async Task<Dictionary<Guid, string>> GetMessagesAsync(IReadOnlyCollection<Guid> transactionIds, CancellationToken cancellationToken = default)
    {
        var ids = transactionIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext.GiftTransactions
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.Message != null)
            .ToDictionaryAsync(t => t.Id, t => t.Message!, cancellationToken);
    }

    public async Task<bool> RemoveMessageAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        // One statement, so a second removal (or a race with one) finds nothing left and says so.
        await dbContext.GiftTransactions
            .Where(t => t.Id == transactionId && t.Message != null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Message, (string?)null), cancellationToken) > 0;
}
