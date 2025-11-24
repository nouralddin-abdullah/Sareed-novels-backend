using Domain.Entities;

namespace Domain.Repositories;

public interface IPrivilegeSubscriptionRepository
{
    /// <summary>
    /// Create a new privilege subscription
    /// </summary>
    Task<NovelPrivilegeSubscription> CreateAsync(NovelPrivilegeSubscription subscription);
    
    /// <summary>
    /// Get active subscription for a user and novel
    /// </summary>
    Task<NovelPrivilegeSubscription?> GetActiveSubscriptionAsync(Guid novelId, string userId);
    
    /// <summary>
    /// Get all subscriptions for a user
    /// </summary>
    Task<(IEnumerable<NovelPrivilegeSubscription>, int)> GetUserSubscriptionsAsync(
        string userId, 
        int pageNumber, 
        int pageSize,
        bool includeExpired = false);
    
    /// <summary>
    /// Get all subscribers for a novel
    /// </summary>
    Task<(IEnumerable<NovelPrivilegeSubscription>, int)> GetNovelSubscribersAsync(
        Guid novelId, 
        int pageNumber, 
        int pageSize,
        bool includeExpired = false);
    
    /// <summary>
    /// Update subscription (e.g., cancel, extend)
    /// </summary>
    Task<bool> UpdateAsync(NovelPrivilegeSubscription subscription);
    
    /// <summary>
    /// Check if user has active subscription for a novel
    /// </summary>
    Task<bool> HasActiveSubscriptionAsync(Guid novelId, string userId);
    
    /// <summary>
    /// Get expired subscriptions that need cleanup
    /// </summary>
    Task<List<NovelPrivilegeSubscription>> GetExpiredSubscriptionsAsync();
}
