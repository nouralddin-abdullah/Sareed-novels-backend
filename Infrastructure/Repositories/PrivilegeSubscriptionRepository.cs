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
        // Subscriptions to a deleted novel are left out of the count as well as the page (the Include's join already
        // dropped them from the page, since Novel has a query filter).
        var query = dbContext.NovelPrivilegeSubscriptions
            .Include(s => s.Novel)
            .Where(s => s.UserId == userId && !s.Novel.IsDeleted);

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

        // Newest first; the id breaks ties, so pages never repeat or skip a subscriber.
        var subscriptions = await query
            .OrderByDescending(s => s.SubscribedAt)
            .ThenByDescending(s => s.Id)
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

    public async Task<int> CountActiveSubscribersAsync(Guid novelId) =>
        await dbContext.NovelPrivilegeSubscriptions.CountAsync(s => s.NovelId == novelId && s.IsActive);

    public async Task<bool> HasActiveSubscriptionAsync(Guid novelId, string userId)
    {
        return await dbContext.NovelPrivilegeSubscriptions
            .AnyAsync(s => 
                s.NovelId == novelId && 
                s.UserId == userId && 
                s.IsActive); // No expiration check - permanent subscriptions!
    }

    public async Task<DateTime?> GetActiveSubscriptionDateAsync(Guid novelId, string userId) =>
        await dbContext.NovelPrivilegeSubscriptions
            .Where(s => s.NovelId == novelId && s.UserId == userId && s.IsActive)
            .OrderBy(s => s.SubscribedAt)
            .Select(s => (DateTime?)s.SubscribedAt)
            .FirstOrDefaultAsync();

    public async Task<List<NovelPrivilegeSubscription>> GetExpiredSubscriptionsAsync()
    {
        // No expiration for permanent subscriptions - return empty list
        return new List<NovelPrivilegeSubscription>();
    }
}
