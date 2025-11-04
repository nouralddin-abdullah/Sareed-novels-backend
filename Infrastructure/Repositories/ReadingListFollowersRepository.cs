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
        dbContext.ReadingListFollowers.Add(follower);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UnfollowAsync(Guid readingListId, string userId)
    {
        var follower = await GetAsync(readingListId, userId);
        if (follower == null) return false;

        dbContext.ReadingListFollowers.Remove(follower);
        return await dbContext.SaveChangesAsync() > 0;
    }

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