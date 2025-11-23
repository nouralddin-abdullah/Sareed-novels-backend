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

    public async Task RecalculateWeeklyLeaderboard()
    {
        var weekAgo = DateTime.UtcNow.AddDays(-7);

        // Clear existing weekly leaderboard
        await ClearLeaderboard("Weekly");

        // Calculate new weekly leaderboard
        var weeklyStats = await dbContext.GiftTransactions
            .Where(t => t.CreatedAt >= weekAgo)
            .GroupBy(t => t.SenderId)
            .Select(g => new
            {
                UserId = g.Key,
                TotalPoints = g.Sum(t => t.TotalCost),
                TotalGifts = g.Sum(t => t.Count)
            })
            .OrderByDescending(x => x.TotalPoints)
            .ToListAsync();

        // Insert with ranks
        var leaderboard = weeklyStats.Select((stat, index) => new GlobalSupporterLeaderboard
        {
            Id = Guid.NewGuid(),
            UserId = stat.UserId,
            TotalPointsGifted = stat.TotalPoints,
            TotalGiftsCount = stat.TotalGifts,
            Rank = index + 1,
            Period = "Weekly",
            LastUpdated = DateTime.UtcNow
        });

        dbContext.GlobalSupporterLeaderboards.AddRange(leaderboard);
        await dbContext.SaveChangesAsync();
    }

    public async Task RecalculateAllTimeLeaderboard()
    {
        // Clear existing all-time leaderboard
        await ClearLeaderboard("AllTime");

        // Calculate all-time leaderboard
        var allTimeStats = await dbContext.GiftTransactions
            .GroupBy(t => t.SenderId)
            .Select(g => new
            {
                UserId = g.Key,
                TotalPoints = g.Sum(t => t.TotalCost),
                TotalGifts = g.Sum(t => t.Count)
            })
            .OrderByDescending(x => x.TotalPoints)
            .ToListAsync();

        // Insert with ranks
        var leaderboard = allTimeStats.Select((stat, index) => new GlobalSupporterLeaderboard
        {
            Id = Guid.NewGuid(),
            UserId = stat.UserId,
            TotalPointsGifted = stat.TotalPoints,
            TotalGiftsCount = stat.TotalGifts,
            Rank = index + 1,
            Period = "AllTime",
            LastUpdated = DateTime.UtcNow
        });

        dbContext.GlobalSupporterLeaderboards.AddRange(leaderboard);
        await dbContext.SaveChangesAsync();
    }

    public async Task ClearLeaderboard(string period)
    {
        var existing = dbContext.GlobalSupporterLeaderboards.Where(l => l.Period == period);
        dbContext.GlobalSupporterLeaderboards.RemoveRange(existing);
        await dbContext.SaveChangesAsync();
    }
}
