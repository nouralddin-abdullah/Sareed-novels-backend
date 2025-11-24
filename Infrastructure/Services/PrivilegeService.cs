using Application.Services;
using Application.Users.Commands.FollowUser;
using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class PrivilegeService(
    ILogger<PrivilegeService> logger,
    INovelPrivilegeRepository privilegeRepository,
    IPrivilegeSubscriptionRepository subscriptionRepository,
    INovelsRepository novelsRepository,
    IChaptersRepository chaptersRepository,
    IWalletService walletService,
    INotificationService notificationService,
    IUsersRepository usersRepository,
    ITransactionManager transactionManager) : IPrivilegeService
{
    // ===== QUERY OPERATIONS =====
    
    public async Task<List<Chapter>> GetLockedChaptersAsync(Guid novelId)
    {
        var privilege = await privilegeRepository.GetByNovelIdAsync(novelId);
        if (privilege == null || !privilege.IsEnabled || privilege.CurrentLockedCount <= 0)
        {
            return new List<Chapter>();
        }
        
        // Get all published chapters ordered by PublishedChapterSequence
        var publishedChapters = await chaptersRepository.GetChaptersReaderView(novelId);
        var orderedChapters = publishedChapters
            .Where(c => c.PublishedChapterSequence.HasValue)
            .OrderBy(c => c.PublishedChapterSequence)
            .ToList();
        
        var totalPublished = orderedChapters.Count;
        
        // Check if novel meets minimum requirement
        if (totalPublished < privilege.MinPublishedRequired)
        {
            logger.LogWarning(
                "Novel {NovelId} has privilege enabled but only {Count} published chapters (min: {Min})",
                novelId, totalPublished, privilege.MinPublishedRequired);
            return new List<Chapter>();
        }
        
        // Use PrivilegeStartSequence if set, otherwise calculate from CurrentLockedCount
        List<Chapter> lockedChapters;
        
        if (privilege.PrivilegeStartSequence.HasValue)
        {
            // Lock chapters from PrivilegeStartSequence onwards (up to CurrentLockedCount)
            lockedChapters = orderedChapters
                .Where(c => c.PublishedChapterSequence >= privilege.PrivilegeStartSequence.Value)
                .Take(privilege.CurrentLockedCount)
                .ToList();
        }
        else
        {
            // Fallback: Lock the LAST N published chapters (sliding window)
            var lockedCount = Math.Min(privilege.CurrentLockedCount, totalPublished);
            lockedChapters = orderedChapters
                .TakeLast(lockedCount)
                .ToList();
        }
        
        logger.LogDebug(
            "Novel {NovelId}: {LockedCount} chapters locked out of {TotalPublished} (Start sequence: {StartSeq})",
            novelId, lockedChapters.Count, totalPublished, privilege.PrivilegeStartSequence);
        
        return lockedChapters;
    }
    
    /// <summary>
    /// Fast check: Is a chapter locked based on its PublishedChapterSequence?
    /// Does NOT load all chapters - just compares sequence numbers.
    /// </summary>
    public bool IsChapterLockedBySequence(int publishedChapterSequence, NovelPrivilege? privilege)
    {
        if (privilege == null || !privilege.IsEnabled || privilege.CurrentLockedCount <= 0)
            return false;
        
        if (!privilege.PrivilegeStartSequence.HasValue)
            return false; // Cannot determine without start sequence
        
        // Simple range check: is chapter sequence >= start sequence?
        // AND is it within the locked count range?
        return publishedChapterSequence >= privilege.PrivilegeStartSequence.Value;
    }
    
    public async Task<bool> IsChapterLockedAsync(Guid chapterId, string? userId = null)
    {
        var chapter = await chaptersRepository.GetChapterById(chapterId);
        if (chapter == null || chapter.Status != "Published" || !chapter.PublishedChapterSequence.HasValue)
            return false;
        
        // Check if user has subscription (bypasses all locks)
        if (!string.IsNullOrEmpty(userId))
        {
            var hasSubscription = await HasActiveSubscriptionAsync(chapter.NovelId, userId);
            if (hasSubscription)
            {
                logger.LogDebug(
                    "User {UserId} has subscription to novel {NovelId}, chapter {ChapterId} unlocked",
                    userId, chapter.NovelId, chapterId);
                return false;
            }
        }
        
        // ✅ OPTIMIZED: Get privilege config only (no chapter loading)
        var privilege = await privilegeRepository.GetByNovelIdAsync(chapter.NovelId);
        
        // ✅ Fast sequence-based check (no database query!)
        var isLocked = IsChapterLockedBySequence(chapter.PublishedChapterSequence.Value, privilege);
        
        if (isLocked)
        {
            logger.LogDebug(
                "Chapter {ChapterId} (seq {Seq}) is privilege-locked for novel {NovelId}",
                chapterId, chapter.PublishedChapterSequence.Value, chapter.NovelId);
        }
        
        return isLocked;
    }
    
    public async Task<NovelPrivilege?> GetPrivilegeConfigAsync(Guid novelId)
    {
        return await privilegeRepository.GetByNovelIdAsync(novelId);
    }
    
    public async Task<bool> HasActiveSubscriptionAsync(Guid novelId, string userId)
    {
        return await subscriptionRepository.HasActiveSubscriptionAsync(novelId, userId);
    }
    
    // ===== AUTHOR OPERATIONS =====
    
    public async Task<OperationResult> EnablePrivilegeAsync(
        Guid novelId, 
        string authorId, 
        decimal subscriptionCost,
        int? privilegeStartSequence = null)
    {
        // Validate novel ownership
        var novel = await novelsRepository.GetOne(novelId);
        if (novel == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Novel not found"
            };
        }
        
        if (novel.AuthorId != authorId)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You don't own this novel"
            };
        }
        
        // Check if privilege already exists
        var existingPrivilege = await privilegeRepository.GetByNovelIdAsync(novelId);
        if (existingPrivilege != null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Privilege system already configured for this novel"
            };
        }
        
        // Validate subscription cost
        if (subscriptionCost < 100 || subscriptionCost > 2000)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Subscription cost must be between 100 and 2000 points"
            };
        }
        
        // Check published chapter count
        var publishedCount = await novelsRepository.GetPublishedChaptersCountAsync(novelId);
        if (publishedCount < 11)
        {
            return new OperationResult
            {
                Success = false,
                Message = $"You need at least 11 published chapters to enable privilege (current: {publishedCount}). The first 10 chapters must remain free for readers."
            };
        }
        
        // Validate privilege start sequence if provided
        if (privilegeStartSequence.HasValue)
        {
            if (privilegeStartSequence.Value < 1 || privilegeStartSequence.Value > publishedCount)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = $"Invalid privilege start sequence. Must be between 1 and {publishedCount}"
                };
            }
            
            // BUSINESS RULE: First 10 chapters must always be free
            if (privilegeStartSequence.Value <= 10)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = "The first 10 chapters must remain free for readers. Privilege can only start from chapter 11 onwards."
                };
            }
            
            // Calculate locked count based on start sequence
            var lockedCount = publishedCount - privilegeStartSequence.Value + 1;
            
            // Ensure locked count doesn't exceed max (20)
            if (lockedCount > 20)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = $"Starting from sequence {privilegeStartSequence.Value} would lock {lockedCount} chapters. Maximum is 20. Please start from sequence {publishedCount - 19} or later."
                };
            }
            
            // Create privilege with specific start sequence
            var privilege = new NovelPrivilege
            {
                Id = Guid.NewGuid(),
                NovelId = novelId,
                IsEnabled = true,
                SubscriptionCost = subscriptionCost,
                CurrentLockedCount = lockedCount,
                PrivilegeStartSequence = privilegeStartSequence.Value,
                MaxLockedChapters = 20,
                MinPublishedRequired = 11,
                CreatedAt = DateTime.UtcNow
            };
            
            await privilegeRepository.CreateAsync(privilege);
            
            logger.LogInformation(
                "Privilege enabled for novel {NovelId} by author {AuthorId}: Starting from sequence {StartSeq}, {LockedCount} chapters locked, cost: {Cost}",
                novelId, authorId, privilegeStartSequence.Value, lockedCount, subscriptionCost);
            
            return new OperationResult
            {
                Success = true,
                Message = $"Privilege system enabled! Chapters {privilegeStartSequence.Value}-{publishedCount} ({lockedCount} chapters) are now locked. Subscription cost: {subscriptionCost} points"
            };
        }
        else
        {
            // Default behavior: lock last 20 (or fewer) chapters, but ensure first 10 are free
            var initialLockedCount = Math.Min(20, publishedCount - 10); // Leave first 10 free
            
            // If less than 11 chapters, cannot lock anything
            if (initialLockedCount <= 0)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = $"Cannot enable privilege with only {publishedCount} chapters. The first 10 chapters must remain free. You need at least 11 published chapters."
                };
            }
            
            var startSequence = publishedCount - initialLockedCount + 1;
            
            // Ensure start sequence is at least 11
            if (startSequence < 11)
            {
                startSequence = 11;
                initialLockedCount = publishedCount - startSequence + 1;
            }
            
            var privilege = new NovelPrivilege
            {
                Id = Guid.NewGuid(),
                NovelId = novelId,
                IsEnabled = true,
                SubscriptionCost = subscriptionCost,
                CurrentLockedCount = initialLockedCount,
                PrivilegeStartSequence = startSequence,
                MaxLockedChapters = 20,
                MinPublishedRequired = 11,
                CreatedAt = DateTime.UtcNow
            };
            
            await privilegeRepository.CreateAsync(privilege);
            
            logger.LogInformation(
                "Privilege enabled for novel {NovelId} by author {AuthorId}: {LockedCount} chapters locked (starting from seq {StartSeq}), cost: {Cost}",
                novelId, authorId, initialLockedCount, startSequence, subscriptionCost);
            
            return new OperationResult
            {
                Success = true,
                Message = $"Privilege system enabled! Chapters {startSequence}-{publishedCount} ({initialLockedCount} chapters) are now locked. The first 10 chapters remain free. Subscription cost: {subscriptionCost} points"
            };
        }
    }
    
    public async Task<OperationResult> UpdatePrivilegeConfigAsync(
        Guid novelId, 
        string authorId, 
        decimal? newSubscriptionCost = null,
        int? newPrivilegeStartSequence = null)
    {
        // Validate novel ownership
        var novel = await novelsRepository.GetOne(novelId);
        if (novel == null || novel.AuthorId != authorId)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Novel not found or you don't own it"
            };
        }
        
        var privilege = await privilegeRepository.GetByNovelIdAsync(novelId);
        if (privilege == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Privilege system not enabled for this novel"
            };
        }
        
        var updated = false;
        
        // Update subscription cost
        if (newSubscriptionCost.HasValue)
        {
            if (newSubscriptionCost.Value < 100 || newSubscriptionCost.Value > 2000)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = "Subscription cost must be between 100 and 2000 points"
                };
            }
            
            privilege.SubscriptionCost = newSubscriptionCost.Value;
            updated = true;
        }
        
        // Update privilege start sequence (move forward only!)
        if (newPrivilegeStartSequence.HasValue)
        {
            var totalPublished = await novelsRepository.GetPublishedChaptersCountAsync(novelId);
            
            // Validate new start sequence
            if (newPrivilegeStartSequence.Value < 1 || newPrivilegeStartSequence.Value > totalPublished)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = $"Invalid privilege start sequence. Must be between 1 and {totalPublished}"
                };
            }
            
            // BUSINESS RULE: First 10 chapters must always be free
            if (newPrivilegeStartSequence.Value <= 10)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = "The first 10 chapters must remain free for readers. Privilege can only start from chapter 11 onwards."
                };
            }
            
            // Check if we have a current start sequence
            if (!privilege.PrivilegeStartSequence.HasValue)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = "Cannot update privilege start sequence - current configuration doesn't have a start sequence set"
                };
            }
            
            // Prevent moving BACKWARD (re-locking chapters)
            if (newPrivilegeStartSequence.Value < privilege.PrivilegeStartSequence.Value)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = $"Cannot move privilege start backward (from {privilege.PrivilegeStartSequence.Value} to {newPrivilegeStartSequence.Value}). This would re-lock previously unlocked chapters. You can only move it forward."
                };
            }
            
            // Prevent moving to same value
            if (newPrivilegeStartSequence.Value == privilege.PrivilegeStartSequence.Value)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = $"Privilege start is already at sequence {privilege.PrivilegeStartSequence.Value}"
                };
            }
            
            // Calculate new locked count
            var oldStartSequence = privilege.PrivilegeStartSequence.Value;
            var newLockedCount = totalPublished - newPrivilegeStartSequence.Value + 1;
            
            // Ensure we don't exceed max locked chapters (should never happen when moving forward)
            if (newLockedCount > privilege.MaxLockedChapters)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = $"Moving to sequence {newPrivilegeStartSequence.Value} would lock {newLockedCount} chapters. Maximum is {privilege.MaxLockedChapters}."
                };
            }
            
            // Calculate how many chapters are being unlocked
            var chaptersUnlocked = newPrivilegeStartSequence.Value - oldStartSequence;
            
            // Update privilege
            privilege.PrivilegeStartSequence = newPrivilegeStartSequence.Value;
            privilege.CurrentLockedCount = Math.Max(0, newLockedCount);
            updated = true;
            
            logger.LogInformation(
                "Privilege start moved forward for novel {NovelId}: {OldStart} → {NewStart}, unlocked {UnlockedCount} chapters, {LockedCount} now locked",
                novelId, oldStartSequence, newPrivilegeStartSequence.Value, chaptersUnlocked, privilege.CurrentLockedCount);
        }
        
        if (updated)
        {
            await privilegeRepository.UpdateAsync(privilege);
            
            logger.LogInformation(
                "Privilege config updated for novel {NovelId} by author {AuthorId}",
                novelId, authorId);
            
            return new OperationResult
            {
                Success = true,
                Message = "Privilege configuration updated successfully"
            };
        }
        
        return new OperationResult
        {
            Success = false,
            Message = "No changes were made"
        };
    }
    
    public async Task<OperationResult> ManuallyUnlockChapterAsync(Guid chapterId, string authorId)
    {
        var chapter = await chaptersRepository.GetChapterById(chapterId);
        if (chapter == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Chapter not found"
            };
        }
        
        var novel = await novelsRepository.GetOne(chapter.NovelId);
        if (novel == null || novel.AuthorId != authorId)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You don't own this novel"
            };
        }
        
        var privilege = await privilegeRepository.GetByNovelIdAsync(chapter.NovelId);
        if (privilege == null || !privilege.IsEnabled)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Privilege system not enabled for this novel"
            };
        }
        
        // Check if chapter is actually locked
        var lockedChapters = await GetLockedChaptersAsync(chapter.NovelId);
        if (!lockedChapters.Any(c => c.Id == chapterId))
        {
            return new OperationResult
            {
                Success = false,
                Message = "This chapter is not locked"
            };
        }
        
        // Decrease locked count by 1
        if (privilege.CurrentLockedCount > 0)
        {
            privilege.CurrentLockedCount--;
            await privilegeRepository.UpdateAsync(privilege);
            
            logger.LogInformation(
                "Author {AuthorId} manually unlocked chapter {ChapterId} for novel {NovelId}. Locked count: {Count}",
                authorId, chapterId, chapter.NovelId, privilege.CurrentLockedCount);
            
            return new OperationResult
            {
                Success = true,
                Message = $"Chapter unlocked! {privilege.CurrentLockedCount} chapters remain locked"
            };
        }
        
        return new OperationResult
        {
            Success = false,
            Message = "No locked chapters to unlock"
        };
    }
    
    // ===== READER OPERATIONS =====
    
    public async Task<OperationResult> SubscribeToPrivilegeAsync(Guid novelId, string userId)
    {
        var privilege = await privilegeRepository.GetByNovelIdAsync(novelId);
        if (privilege == null || !privilege.IsEnabled)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Privilege system not enabled for this novel"
            };
        }
        
        // Prevent authors from subscribing to their own novel
        var novel = await novelsRepository.GetOne(novelId);
        if (novel != null && novel.AuthorId == userId)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You cannot subscribe to your own novel's privilege system. You already have full access to all chapters."
            };
        }
        
        // Check if already subscribed
        var existingSubscription = await subscriptionRepository.GetActiveSubscriptionAsync(novelId, userId);
        if (existingSubscription != null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You are already subscribed to this novel's privilege"
            };
        }
        
        var cost = privilege.SubscriptionCost;
        
        // Check sufficient balance
        if (!await walletService.HasSufficientBalanceAsync(userId, cost))
        {
            return new OperationResult
            {
                Success = false,
                Message = $"Insufficient balance. Required: {cost} points"
            };
        }
        
        // ✅ START TRANSACTION: Ensures all-or-nothing for payment + subscription
        await using var transaction = await transactionManager.BeginTransactionAsync();
        
        try
        {
            // Step 1: Transfer points atomically (subscriber -> author)
            await walletService.TransferPointsAsync(
                fromUserId: userId,
                toUserId: novel.AuthorId,
                amount: cost,
                fromTransactionType: TransactionType.PrivilegeSubscription,
                toTransactionType: TransactionType.PrivilegeRevenue,
                fromDescription: $"Subscribed to privilege for novel: {novel.Title}",
                toDescription: $"Privilege subscription revenue from novel: {novel.Title}"
            );
            
            // Step 2: Create subscription record (PERMANENT)
            var subscription = new NovelPrivilegeSubscription
            {
                Id = Guid.NewGuid(),
                NovelId = novelId,
                UserId = userId,
                SubscribedAt = DateTime.UtcNow,
                IsActive = true,
                AmountPaid = cost
            };
            
            await subscriptionRepository.CreateAsync(subscription);
            
            // ✅ COMMIT TRANSACTION: Make all changes permanent
            await transaction.CommitAsync();
            
            logger.LogInformation(
                "User {UserId} subscribed to privilege for novel {NovelId}: {Cost} points (PERMANENT)",
                userId, novelId, cost);
            
            // Step 3: Send notification AFTER transaction commits (best effort)
            _ = Task.Run(async () =>
            {
                try
                {
                    var subscriber = await usersRepository.GetUserById(userId);
                    if (subscriber != null)
                    {
                        await notificationService.SendPrivilegeSubscribedNotification(
                            novel.AuthorId,
                            subscriber,
                            novel,
                            cost
                        );
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to send privilege subscription notification (non-critical)");
                }
            });
            
            return new OperationResult
            {
                Success = true,
                Message = $"Subscribed successfully! All privilege chapters are now unlocked. (Cost: {cost} points, PERMANENT)"
            };
        }
        catch (Exception ex)
        {
            // ❌ ROLLBACK: Undo everything if any step fails
            await transaction.RollbackAsync();
            logger.LogError(ex, "Failed to subscribe to privilege: {Message}", ex.Message);
            
            return new OperationResult
            {
                Success = false,
                Message = "Subscription failed. No points were deducted. Please try again."
            };
        }
    }
    
    public async Task<OperationResult> CancelSubscriptionAsync(Guid novelId, string userId)
    {
        var subscription = await subscriptionRepository.GetActiveSubscriptionAsync(novelId, userId);
        if (subscription == null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You don't have an active subscription to this novel"
            };
        }
        
        subscription.IsActive = false;
        subscription.CancelledAt = DateTime.UtcNow;
        subscription.CancellationReason = "UserCancelled";
        
        await subscriptionRepository.UpdateAsync(subscription);
        
        logger.LogInformation(
            "User {UserId} cancelled privilege subscription for novel {NovelId}",
            userId, novelId);
        
        return new OperationResult
        {
            Success = true,
            Message = "Subscription cancelled. You will no longer have access to privilege chapters. (No refund)"
        };
    }
    
    // ===== INTERNAL TRIGGERS =====
    
    public async Task OnChapterPublishedAsync(Guid novelId)
    {
        var privilege = await privilegeRepository.GetByNovelIdAsync(novelId);
        if (privilege == null || !privilege.IsEnabled)
            return;
        
        var totalPublished = await novelsRepository.GetPublishedChaptersCountAsync(novelId);
        
        // If we have less than max (20), add 1
        if (privilege.CurrentLockedCount < privilege.MaxLockedChapters)
        {
            privilege.CurrentLockedCount++;
            await privilegeRepository.UpdateAsync(privilege);
            
            logger.LogInformation(
                "Chapter published for novel {NovelId}: Locked count increased to {Count}",
                novelId, privilege.CurrentLockedCount);
        }
        // If we're at max (20), maintain the sliding window
        // New chapter extends the lock range, oldest locked chapter becomes unlocked automatically
        else
        {
            // Update PrivilegeStartSequence to shift the window forward
            if (privilege.PrivilegeStartSequence.HasValue)
            {
                privilege.PrivilegeStartSequence++;
                await privilegeRepository.UpdateAsync(privilege);
                
                logger.LogInformation(
                    "Chapter published for novel {NovelId}: Locked count at max ({Max}), sliding window shifted to start at sequence {NewStart}",
                    novelId, privilege.MaxLockedChapters, privilege.PrivilegeStartSequence);
            }
            else
            {
                logger.LogInformation(
                    "Chapter published for novel {NovelId}: Locked count at max ({Max}), sliding window maintained",
                    novelId, privilege.MaxLockedChapters);
            }
        }
    }
    
    public async Task OnChapterDeletedAsync(Guid novelId, int deletedChapterSequence)
    {
        var privilege = await privilegeRepository.GetByNovelIdAsync(novelId);
        if (privilege == null || !privilege.IsEnabled)
            return;
        
        // Check if the deleted chapter was actually in the locked range
        var wasLocked = false;
        
        if (privilege.PrivilegeStartSequence.HasValue)
        {
            // Check if deleted chapter sequence was >= start sequence
            // (meaning it was in the locked range)
            wasLocked = deletedChapterSequence >= privilege.PrivilegeStartSequence.Value;
            
            // If deleted chapter was BEFORE privilege start, shift the start sequence DOWN by 1
            if (deletedChapterSequence < privilege.PrivilegeStartSequence.Value)
            {
                privilege.PrivilegeStartSequence--;
                await privilegeRepository.UpdateAsync(privilege);
                
                logger.LogInformation(
                    "Published UNLOCKED chapter (seq {Sequence}) deleted BEFORE privilege start for novel {NovelId}: Privilege start shifted from {OldStart} to {NewStart}",
                    deletedChapterSequence, novelId, privilege.PrivilegeStartSequence + 1, privilege.PrivilegeStartSequence);
                
                return; // Don't decrease locked count - it was unlocked
            }
        }
        else
        {
            // Fallback: check if chapter was in the last N chapters
            var totalPublished = await novelsRepository.GetPublishedChaptersCountAsync(novelId);
            var lockStartSequence = totalPublished - privilege.CurrentLockedCount + 1;
            wasLocked = deletedChapterSequence >= lockStartSequence;
        }
        
        // Only decrease locked count if the deleted chapter was actually locked
        if (wasLocked && privilege.CurrentLockedCount > 0)
        {
            privilege.CurrentLockedCount--;
            await privilegeRepository.UpdateAsync(privilege);
            
            logger.LogInformation(
                "Published LOCKED chapter (seq {Sequence}) deleted for novel {NovelId}: Locked count decreased to {Count}",
                deletedChapterSequence, novelId, privilege.CurrentLockedCount);
        }
        else if (!privilege.PrivilegeStartSequence.HasValue)
        {
            // Log if chapter was unlocked (for fallback case)
            logger.LogInformation(
                "Published UNLOCKED chapter (seq {Sequence}) deleted for novel {NovelId}: Locked count remains {Count}",
                deletedChapterSequence, novelId, privilege.CurrentLockedCount);
        }
    }
    
    public async Task PerformDailyUnlockAsync(Guid? specificNovelId = null)
    {
        List<NovelPrivilege> privileges;
        
        if (specificNovelId.HasValue)
        {
            var privilege = await privilegeRepository.GetByNovelIdAsync(specificNovelId.Value);
            privileges = privilege != null ? new List<NovelPrivilege> { privilege } : new List<NovelPrivilege>();
        }
        else
        {
            privileges = await privilegeRepository.GetAllEnabledPrivilegesAsync();
        }
        
        var unlockedCount = 0;
        
        foreach (var privilege in privileges)
        {
            // Check if already unlocked today
            if (privilege.LastDailyUnlockDate?.Date == DateTime.UtcNow.Date)
            {
                logger.LogDebug(
                    "Novel {NovelId} already had daily unlock today, skipping",
                    privilege.NovelId);
                continue;
            }
            
            // Only unlock if we have locked chapters
            if (privilege.CurrentLockedCount > 0)
            {
                privilege.CurrentLockedCount--;
                privilege.TotalDailyUnlocksPerformed++;
                privilege.LastDailyUnlockDate = DateTime.UtcNow;
                
                await privilegeRepository.UpdateAsync(privilege);
                unlockedCount++;
                
                logger.LogInformation(
                    "Daily unlock for novel {NovelId}: {RemainingLocked} chapters still locked",
                    privilege.NovelId, privilege.CurrentLockedCount);
            }
        }
        
        logger.LogInformation(
            "Daily unlock completed: {UnlockedCount} novels processed",
            unlockedCount);
    }
}
