using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class PrivilegeSubscriptionRepository(ApplicationDbContext dbContext) : IPrivilegeSubscriptionRepository
{
    public async Task<NovelPrivilegeSubscription> CreateAsync(NovelPrivilegeSubscription subscription)
    {
        dbContext.NovelPrivilegeSubscriptions.Add(subscription);
        await dbContext.SaveChangesAsync();
        return subscription;
    }

    public async Task<NovelPrivilegeSubscription?> GetActiveSubscriptionAsync(Guid novelId, string userId)
    {
        return await dbContext.NovelPrivilegeSubscriptions
            .Include(s => s.Novel)
            .FirstOrDefaultAsync(s => 
                s.NovelId == novelId && 
                s.UserId == userId && 
                s.IsActive); // No expiration check - permanent subscriptions!
    }

    public async Task<(IEnumerable<NovelPrivilegeSubscription>, int)> GetUserSubscriptionsAsync(
        string userId, 
        int pageNumber, 
        int pageSize,
        bool includeExpired = false)
    {
        var query = dbContext.NovelPrivilegeSubscriptions
            .Include(s => s.Novel)
            .Where(s => s.UserId == userId);

        if (!includeExpired)
        {
            query = query.Where(s => s.IsActive); // Only check IsActive
        }

        var totalCount = await query.CountAsync();

        var subscriptions = await query
            .OrderByDescending(s => s.SubscribedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (subscriptions, totalCount);
    }

    public async Task<(IEnumerable<NovelPrivilegeSubscription>, int)> GetNovelSubscribersAsync(
        Guid novelId, 
        int pageNumber, 
        int pageSize,
        bool includeExpired = false)
    {
        var query = dbContext.NovelPrivilegeSubscriptions
            .Include(s => s.User)
            .Where(s => s.NovelId == novelId);

        if (!includeExpired)
        {
            query = query.Where(s => s.IsActive); // Only check IsActive
        }

        var totalCount = await query.CountAsync();

        var subscriptions = await query
            .OrderByDescending(s => s.SubscribedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (subscriptions, totalCount);
    }

    public async Task<bool> UpdateAsync(NovelPrivilegeSubscription subscription)
    {
        dbContext.NovelPrivilegeSubscriptions.Update(subscription);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> HasActiveSubscriptionAsync(Guid novelId, string userId)
    {
        return await dbContext.NovelPrivilegeSubscriptions
            .AnyAsync(s => 
                s.NovelId == novelId && 
                s.UserId == userId && 
                s.IsActive); // No expiration check - permanent subscriptions!
    }

    public async Task<List<NovelPrivilegeSubscription>> GetExpiredSubscriptionsAsync()
    {
        // No expiration for permanent subscriptions - return empty list
        return new List<NovelPrivilegeSubscription>();
    }
}
