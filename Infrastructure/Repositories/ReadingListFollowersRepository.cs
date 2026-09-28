using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ReadingListFollowersRepository(ApplicationDbContext dbContext) : IReadingListFollowersRepository
{
    public async Task<ReadingListFollower?> GetAsync(Guid readingListId, string userId)
    {
        return await dbContext.ReadingListFollowers
            .FirstOrDefaultAsync(rlf => rlf.ReadingListId == readingListId && rlf.UserId == userId);
    }

    public async Task<bool> FollowAsync(ReadingListFollower follower)
    {
        // A concurrent duplicate is a no-op (the key lock), not a primary key violation (500).
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ReadingListFollowers (ReadingListId, UserId, FollowedAt)
            SELECT {follower.ReadingListId}, {follower.UserId}, {follower.FollowedAt}
            WHERE NOT EXISTS (SELECT 1 FROM ReadingListFollowers WITH (UPDLOCK, HOLDLOCK)
                              WHERE ReadingListId = {follower.ReadingListId} AND UserId = {follower.UserId})
            """);
        return inserted == 1;
    }

    public async Task<bool> UnfollowAsync(Guid readingListId, string userId) =>
        // One statement: a concurrent unfollow deletes nothing instead of failing on the row the other one removed.
        await dbContext.ReadingListFollowers
            .Where(rlf => rlf.ReadingListId == readingListId && rlf.UserId == userId)
            .ExecuteDeleteAsync() > 0;

    public async Task<bool> IsFollowingAsync(Guid readingListId, string userId)
    {
        return await dbContext.ReadingListFollowers
            .AnyAsync(rlf => rlf.ReadingListId == readingListId && rlf.UserId == userId);
    }

    public async Task<int> GetFollowersCountAsync(Guid readingListId)
    {
        return await dbContext.ReadingListFollowers
            .CountAsync(rlf => rlf.ReadingListId == readingListId);
    }

    public async Task<(IEnumerable<User>, int)> GetFollowersAsync(Guid readingListId, int pageNumber, int pageSize)
    {
        var query = dbContext.ReadingListFollowers
            .Where(rlf => rlf.ReadingListId == readingListId)
            .Select(rlf => rlf.User)
            .OrderByDescending(u => u.CreatedAt);

        var totalCount = await query.CountAsync();

        var users = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (users, totalCount);
    }
}