using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class GlobalSupporterLeaderboardRepository(ApplicationDbContext dbContext) : IGlobalSupporterLeaderboardRepository
{
    public async Task<(IEnumerable<GlobalSupporterLeaderboard> supporters, int totalCount)> GetLeaderboard(string period, int pageNumber, int pageSize)
    {
        var query = dbContext.GlobalSupporterLeaderboards
            .Where(l => l.Period == period)
            .Include(l => l.User)
            .OrderBy(l => l.Rank);

        var totalCount = await query.CountAsync();

        var supporters = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (supporters, totalCount);
    }

    public Task RecalculateWeeklyLeaderboard() => Recalculate("Weekly", since: DateTime.UtcNow.AddDays(-7));

    public Task RecalculateAllTimeLeaderboard() => Recalculate("AllTime", since: null);

    private async Task Recalculate(string period, DateTime? since)
    {
        var gifts = dbContext.GiftTransactions.AsQueryable();
        if (since.HasValue)
        {
            gifts = gifts.Where(t => t.CreatedAt >= since.Value);
        }

        var stats = await gifts
            .GroupBy(t => t.SenderId)
            .Select(g => new
            {
                UserId = g.Key,
                TotalPoints = g.Sum(t => t.TotalCost),
                TotalGifts = g.Sum(t => t.Count)
            })
            .OrderByDescending(x => x.TotalPoints)
            .ThenByDescending(x => x.TotalGifts)
            .ToListAsync();

        var now = DateTime.UtcNow;

        // Swap the board in one transaction: it used to be cleared and refilled in separate commits, so a reader could
        // see it empty, and a failed insert left it empty until the next recalculation.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await dbContext.GlobalSupporterLeaderboards.Where(l => l.Period == period).ExecuteDeleteAsync();
        dbContext.GlobalSupporterLeaderboards.AddRange(stats.Select((stat, index) => new GlobalSupporterLeaderboard
        {
            Id = Guid.NewGuid(),
            UserId = stat.UserId,
            TotalPointsGifted = stat.TotalPoints,
            TotalGiftsCount = stat.TotalGifts,
            Rank = index + 1,
            Period = period,
            LastUpdated = now
        }));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task ClearLeaderboard(string period)
    {
        var existing = dbContext.GlobalSupporterLeaderboards.Where(l => l.Period == period);
        dbContext.GlobalSupporterLeaderboards.RemoveRange(existing);
        await dbContext.SaveChangesAsync();
    }
}
