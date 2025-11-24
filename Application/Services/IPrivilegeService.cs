using Application.Users.Commands.FollowUser;
using Domain.Entities;

namespace Application.Services;

/// <summary>
/// Service for managing novel privilege system (premium chapter locking)
/// </summary>
public interface IPrivilegeService
{
    // ===== QUERY OPERATIONS =====
    
    /// <summary>
    /// Get all locked chapters for a novel (sliding window of last N published chapters)
    /// </summary>
    Task<List<Chapter>> GetLockedChaptersAsync(Guid novelId);
    
    /// <summary>
    /// Check if a specific chapter is locked for a user.
    /// Returns false if user has active subscription.
    /// </summary>
    Task<bool> IsChapterLockedAsync(Guid chapterId, string? userId = null);
    
    /// <summary>
    /// Fast check: Is a chapter locked based on its PublishedChapterSequence?
    /// Does NOT load chapters - just compares sequence numbers.
    /// </summary>
    bool IsChapterLockedBySequence(int publishedChapterSequence, NovelPrivilege? privilege);
    
    /// <summary>
    /// Get privilege configuration for a novel
    /// </summary>
    Task<NovelPrivilege?> GetPrivilegeConfigAsync(Guid novelId);
    
    /// <summary>
    /// Check if user has active subscription to a novel's privilege
    /// </summary>
    Task<bool> HasActiveSubscriptionAsync(Guid novelId, string userId);
    
    // ===== AUTHOR OPERATIONS =====
    
    /// <summary>
    /// Enable privilege system for a novel.
    /// Requires minimum published chapters.
    /// </summary>
    /// <param name="privilegeStartSequence">Optional: PublishedChapterSequence to start locking from. If null, locks last 20 chapters.</param>
    Task<OperationResult> EnablePrivilegeAsync(
        Guid novelId, 
        string authorId, 
        decimal subscriptionCost,
        int? privilegeStartSequence = null);
    
    /// <summary>
    /// Update privilege configuration (e.g., change subscription cost, move start forward)
    /// </summary>
    Task<OperationResult> UpdatePrivilegeConfigAsync(
        Guid novelId, 
        string authorId, 
        decimal? newSubscriptionCost = null,
        int? newPrivilegeStartSequence = null);
    
    /// <summary>
    /// Manually unlock a specific privilege chapter (author override)
    /// </summary>
    Task<OperationResult> ManuallyUnlockChapterAsync(
        Guid chapterId, 
        string authorId);
    
    // ===== READER OPERATIONS =====
    
    /// <summary>
    /// User subscribes to a novel's privilege.
    /// Deducts points from wallet.
    /// Subscription is PERMANENT.
    /// </summary>
    Task<OperationResult> SubscribeToPrivilegeAsync(
        Guid novelId, 
        string userId);
    
    /// <summary>
    /// User cancels their privilege subscription (no refund)
    /// </summary>
    Task<OperationResult> CancelSubscriptionAsync(
        Guid novelId, 
        string userId);
    
    // ===== INTERNAL TRIGGERS (Called by Chapter Handlers) =====
    
    /// <summary>
    /// Called when a new chapter is published.
    /// Extends the privilege lock window (adds 1 to locked count or slides window).
    /// </summary>
    Task OnChapterPublishedAsync(Guid novelId);
    
    /// <summary>
    /// Called when a published chapter is deleted from privilege zone.
    /// Decreases locked count by 1.
    /// </summary>
    Task OnChapterDeletedAsync(Guid novelId, int deletedChapterSequence);
    
    /// <summary>
    /// Daily unlock background job.
    /// Decreases CurrentLockedCount by 1 for all enabled privileges.
    /// </summary>
    Task PerformDailyUnlockAsync(Guid? specificNovelId = null);
}
