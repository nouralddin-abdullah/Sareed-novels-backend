using Domain.Entities;

namespace Domain.Repositories;

public interface INovelPrivilegeRepository
{
    /// <summary>
    /// Get privilege configuration for a novel
    /// </summary>
    Task<NovelPrivilege?> GetByNovelIdAsync(Guid novelId);
    
    /// <summary>
    /// Create privilege configuration for a novel
    /// </summary>
    Task<NovelPrivilege> CreateAsync(NovelPrivilege privilege);
    
    /// <summary>
    /// Update privilege configuration
    /// </summary>
    Task<bool> UpdateAsync(NovelPrivilege privilege);
    
    /// <summary>
    /// Delete privilege configuration (only if no active subscriptions)
    /// </summary>
    Task<bool> DeleteAsync(Guid privilegeId);
    
    /// <summary>
    /// Get all enabled privileges (for daily unlock background job)
    /// </summary>
    Task<List<NovelPrivilege>> GetAllEnabledPrivilegesAsync();
    
    /// <summary>
    /// Check if privilege exists for a novel
    /// </summary>
    Task<bool> ExistsAsync(Guid novelId);
}
