using Application.Services;
using Application.Users.Commands.FollowUser;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application.Common;
using Application.Wallet;

namespace Infrastructure.Services;

public class PrivilegeService(
    ILogger<PrivilegeService> logger,
    INovelPrivilegeRepository privilegeRepository,
    IPrivilegeSubscriptionRepository subscriptionRepository,
    INovelsRepository novelsRepository,
    IChaptersRepository chaptersRepository,
    IWalletService walletService,
    ITransactionManager transactionManager,
    IServiceScopeFactory scopeFactory) : IPrivilegeService
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
                Code = "NovelNotFound",
                Message = "الرواية غير موجودة"
            };
        }
        
        if (novel.AuthorId != authorId)
        {
            return new OperationResult
            {
                Success = false,
                Code = "NotOwner",
                Message = "هذا الإجراء متاح لكاتب الرواية فقط"
            };
        }
        
        // Check if privilege already exists
        var existingPrivilege = await privilegeRepository.GetByNovelIdAsync(novelId);
        if (existingPrivilege != null)
        {
            return new OperationResult
            {
                Success = false,
                Code = "PrivilegeAlreadyEnabled",
                Message = "الوصول المبكر مفعّل لهذه الرواية بالفعل"
            };
        }
        
        // Validate subscription cost
        if (subscriptionCost < 100 || subscriptionCost > 2000)
        {
            return new OperationResult
            {
                Success = false,
                Code = "InvalidSubscriptionCost",
                Message = "سعر الاشتراك يجب أن يكون من 100 إلى 2000 نقطة"
            };
        }
        
        // Check published chapter count
        var publishedCount = await novelsRepository.GetPublishedChaptersCountAsync(novelId);
        if (publishedCount < 11)
        {
            return new OperationResult
            {
                Success = false,
                Code = "NotEnoughPublishedChapters",
                Message = $"يلزم 11 فصلًا منشورًا على الأقل لتفعيل الوصول المبكر (المنشور الآن: {publishedCount}). تبقى الفصول العشرة الأولى مجانية للقرّاء."
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
                    Code = "InvalidPrivilegeStart",
                    Message = $"رقم أول فصل مقفل يجب أن يكون من 1 إلى {publishedCount}"
                };
            }
            
            // BUSINESS RULE: First 10 chapters must always be free
            if (privilegeStartSequence.Value <= 10)
            {
                return new OperationResult
                {
                    Success = false,
                    Code = "FirstChaptersMustStayFree",
                    Message = "تبقى الفصول العشرة الأولى مجانية للقرّاء، فالوصول المبكر يبدأ من الفصل 11 أو بعده."
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
                    Code = "TooManyLockedChapters",
                    Message = $"البدء من الفصل {privilegeStartSequence.Value} يقفل {ArabicCount.ChaptersObject(lockedCount)}، والحد الأقصى 20 فصلًا. ابدأ من الفصل {publishedCount - 19} أو بعده."
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
                Message = $"تم تفعيل الوصول المبكر. الفصول من {privilegeStartSequence.Value} إلى {publishedCount} مقفلة الآن ({ArabicCount.Chapters(lockedCount)})، وسعر الاشتراك {Points.Format(subscriptionCost)} نقطة."
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
                    Code = "NotEnoughPublishedChapters",
                    Message = $"يلزم 11 فصلًا منشورًا على الأقل لتفعيل الوصول المبكر (المنشور الآن: {publishedCount}). تبقى الفصول العشرة الأولى مجانية للقرّاء."
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
                Message = $"تم تفعيل الوصول المبكر. الفصول من {startSequence} إلى {publishedCount} مقفلة الآن ({ArabicCount.Chapters(initialLockedCount)})، وتبقى الفصول العشرة الأولى مجانية. سعر الاشتراك {Points.Format(subscriptionCost)} نقطة."
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
                Code = "NotOwner",
                Message = "هذا الإجراء متاح لكاتب الرواية فقط"
            };
        }
        
        var privilege = await privilegeRepository.GetByNovelIdAsync(novelId);
        if (privilege == null)
        {
            return new OperationResult
            {
                Success = false,
                Code = "PrivilegeNotEnabled",
                Message = "الوصول المبكر غير مفعّل لهذه الرواية"
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
                    Code = "InvalidSubscriptionCost",
                    Message = "سعر الاشتراك يجب أن يكون من 100 إلى 2000 نقطة"
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
                    Code = "InvalidPrivilegeStart",
                    Message = $"رقم أول فصل مقفل يجب أن يكون من 1 إلى {totalPublished}"
                };
            }
            
            // BUSINESS RULE: First 10 chapters must always be free
            if (newPrivilegeStartSequence.Value <= 10)
            {
                return new OperationResult
                {
                    Success = false,
                    Code = "FirstChaptersMustStayFree",
                    Message = "تبقى الفصول العشرة الأولى مجانية للقرّاء، فالوصول المبكر يبدأ من الفصل 11 أو بعده."
                };
            }
            
            // Check if we have a current start sequence
            if (!privilege.PrivilegeStartSequence.HasValue)
            {
                return new OperationResult
                {
                    Success = false,
                    Code = "NoPrivilegeStart",
                    Message = "لا يمكن تغيير أول فصل مقفل، فإعدادات الوصول المبكر الحالية لا تحدده"
                };
            }
            
            // Prevent moving BACKWARD (re-locking chapters)
            if (newPrivilegeStartSequence.Value < privilege.PrivilegeStartSequence.Value)
            {
                return new OperationResult
                {
                    Success = false,
                    Code = "PrivilegeStartCannotMoveBack",
                    Message = $"لا يمكن إرجاع بداية الفصول المقفلة من الفصل {privilege.PrivilegeStartSequence.Value} إلى الفصل {newPrivilegeStartSequence.Value}، فهذا يقفل فصولًا فُتحت للقرّاء من قبل. يمكن تقديمها فقط."
                };
            }
            
            // Prevent moving to same value
            if (newPrivilegeStartSequence.Value == privilege.PrivilegeStartSequence.Value)
            {
                return new OperationResult
                {
                    Success = false,
                    Code = "NoChanges",
                    Message = $"الفصول المقفلة تبدأ من الفصل {privilege.PrivilegeStartSequence.Value} بالفعل"
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
                    Code = "TooManyLockedChapters",
                    Message = $"البدء من الفصل {newPrivilegeStartSequence.Value} يقفل {ArabicCount.ChaptersObject(newLockedCount)}، والحد الأقصى {ArabicCount.Chapters(privilege.MaxLockedChapters)}."
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
                Message = "حُفظت إعدادات الوصول المبكر"
            };
        }
        
        return new OperationResult
        {
            Success = false,
            Code = "NoChanges",
            Message = "لم يتغير شيء في الإعدادات"
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
                Code = "ChapterNotFound",
                Message = "الفصل غير موجود"
            };
        }
        
        var novel = await novelsRepository.GetOne(chapter.NovelId);
        if (novel == null || novel.AuthorId != authorId)
        {
            return new OperationResult
            {
                Success = false,
                Code = "NotOwner",
                Message = "هذا الإجراء متاح لكاتب الرواية فقط"
            };
        }
        
        var privilege = await privilegeRepository.GetByNovelIdAsync(chapter.NovelId);
        if (privilege == null || !privilege.IsEnabled)
        {
            return new OperationResult
            {
                Success = false,
                Code = "PrivilegeNotEnabled",
                Message = "الوصول المبكر غير مفعّل لهذه الرواية"
            };
        }
        
        // Readers see a chapter as locked when its sequence is at or past PrivilegeStartSequence, so unlocking means
        // moving the start past it. (This used to only decrement CurrentLockedCount, which readers never check, so
        // the author got "Chapter unlocked!" while the chapter stayed locked.) Earlier locked chapters are unlocked
        // with it: the model is a single locked range, it can't leave holes.
        if (chapter.Status != "Published"
            || !chapter.PublishedChapterSequence.HasValue
            || !IsChapterLockedBySequence(chapter.PublishedChapterSequence.Value, privilege))
        {
            return new OperationResult
            {
                Success = false,
                Code = "ChapterNotLocked",
                Message = "هذا الفصل غير مقفل"
            };
        }

        var sequence = chapter.PublishedChapterSequence.Value;
        var oldStart = privilege.PrivilegeStartSequence!.Value;
        var unlockedCount = sequence - oldStart + 1;

        privilege.PrivilegeStartSequence = sequence + 1;
        privilege.CurrentLockedCount = Math.Max(0, privilege.CurrentLockedCount - unlockedCount);
        await privilegeRepository.UpdateAsync(privilege);

        logger.LogInformation(
            "Author {AuthorId} manually unlocked chapter {ChapterId} (seq {Sequence}) for novel {NovelId}: start {OldStart} -> {NewStart}, {Count} still locked",
            authorId, chapterId, sequence, chapter.NovelId, oldStart, privilege.PrivilegeStartSequence, privilege.CurrentLockedCount);

        return new OperationResult
        {
            Success = true,
            Message = unlockedCount == 1
                ? $"فُتح الفصل للجميع. الفصول المقفلة الآن: {privilege.CurrentLockedCount}"
                : $"فُتحت الفصول من {oldStart} إلى {sequence} للجميع. الفصول المقفلة الآن: {privilege.CurrentLockedCount}"
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
                Code = "PrivilegeNotEnabled",
                Message = "الوصول المبكر غير مفعّل لهذه الرواية"
            };
        }
        
        var novel = await novelsRepository.GetOne(novelId);
        if (novel == null)
        {
            return new OperationResult
            {
                Success = false,
                Code = "NovelNotFound",
                Message = "الرواية غير موجودة"
            };
        }

        // Prevent authors from subscribing to their own novel
        if (novel.AuthorId == userId)
        {
            return new OperationResult
            {
                Success = false,
                Code = "CannotSubscribeToOwnNovel",
                Message = "لا يمكنك الاشتراك في الوصول المبكر لروايتك، فكل فصولها متاحة لك"
            };
        }
        
        // Check if already subscribed
        var existingSubscription = await subscriptionRepository.GetActiveSubscriptionAsync(novelId, userId);
        if (existingSubscription != null)
        {
            return new OperationResult
            {
                Success = false,
                Code = "AlreadySubscribed",
                Message = "أنت مشترك في الوصول المبكر لهذه الرواية بالفعل"
            };
        }
        
        var cost = privilege.SubscriptionCost;
        
        // Check sufficient balance
        if (!await walletService.HasSufficientBalanceAsync(userId, cost))
        {
            return new OperationResult
            {
                Success = false,
                Code = "InsufficientBalance",
                Message = $"رصيدك من النقاط غير كافٍ. سعر الاشتراك {Points.Format(cost)} نقطة."
            };
        }
        
        // Payment and subscription commit together or not at all. Both ledger rows point at the subscription: that pairs
        // the reader's payment with the author's earning, which a refund of the points behind it takes back while it is
        // still on hold (#22). If SQL Server picks it as a deadlock victim, none of it happened, and it runs once more in a
        // new transaction (#27): the reader pays once either way.
        var subscriptionId = Guid.NewGuid();
        try
        {
            await transactionManager.InNewTransactionAsync(async _ =>
            {
                // Step 1: Transfer points atomically (subscriber -> author)
                await walletService.TransferPointsAsync(
                    fromUserId: userId,
                    toUserId: novel.AuthorId,
                    amount: cost,
                    fromTransactionType: TransactionType.PrivilegeSubscription,
                    toTransactionType: TransactionType.PrivilegeRevenue,
                    fromDescription: TransactionDescriptions.PrivilegeSubscription(novel.Title),
                    toDescription: TransactionDescriptions.PrivilegeRevenue(novel.Title),
                    relatedRequestId: subscriptionId,
                    details: new TransactionDetails(NovelId: novel.Id)
                );

                // The transfer holds both wallet rows locked until commit, so a concurrent subscribe by the same user
                // waits here and then sees this subscription; re-checking now stops a double-click from paying twice.
                if (await subscriptionRepository.HasActiveSubscriptionAsync(novelId, userId))
                {
                    throw new AlreadySubscribedException(); // rolls the payment back
                }

                // Step 2: Create subscription record (PERMANENT)
                await subscriptionRepository.CreateAsync(new NovelPrivilegeSubscription
                {
                    Id = subscriptionId,
                    NovelId = novelId,
                    UserId = userId,
                    SubscribedAt = DateTime.UtcNow,
                    IsActive = true,
                    AmountPaid = cost
                });
                return true;
            }, attempts: 2);
        }
        catch (AlreadySubscribedException)
        {
            return new OperationResult
            {
                Success = false,
                Code = "AlreadySubscribed",
                Message = "أنت مشترك في الوصول المبكر لهذه الرواية بالفعل"
            };
        }
        catch (InsufficientBalanceException)
        {
            return new OperationResult
            {
                Success = false,
                Code = "InsufficientBalance",
                Message = $"رصيدك من النقاط غير كافٍ. سعر الاشتراك {Points.Format(cost)} نقطة."
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to subscribe to privilege: {Message}", ex.Message);

            return new OperationResult
            {
                Success = false,
                Code = "OperationFailed",
                Message = "تعذّر الاشتراك، ولم تُخصم أي نقاط. حاول مرة أخرى."
            };
        }

        logger.LogInformation(
            "User {UserId} subscribed to privilege for novel {NovelId}: {Cost} points (permanent)",
            userId, novelId, cost);

        // Notify AFTER the commit (best effort), in a scope of its own: the task outlives the request, whose DbContext
        // (which this used to share) is disposed when the request ends.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var usersRepository = scope.ServiceProvider.GetRequiredService<IUsersRepository>();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var subscriber = await usersRepository.GetUserById(userId);
                if (subscriber != null)
                {
                    await notificationService.SendPrivilegeSubscribedNotification(novel.AuthorId, subscriber, novel);
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
            Message = $"تم الاشتراك في الوصول المبكر. فُتحت لك الفصول المقفلة في هذه الرواية بشكل دائم، مقابل {Points.Format(cost)} نقطة."
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
            if (privilege.LastDailyUnlockDate?.Date == DateTime.UtcNow.Date)
            {
                logger.LogDebug(
                    "Novel {NovelId} already had daily unlock today, skipping",
                    privilege.NovelId);
                continue;
            }
            
            if (privilege.CurrentLockedCount > 0 && privilege.PrivilegeStartSequence.HasValue)
            {
                privilege.PrivilegeStartSequence++;
                privilege.CurrentLockedCount--;
                privilege.TotalDailyUnlocksPerformed++;
                privilege.LastDailyUnlockDate = DateTime.UtcNow;
                
                await privilegeRepository.UpdateAsync(privilege);
                unlockedCount++;
                
                logger.LogInformation(
                    "Daily unlock for novel {NovelId}: Start sequence moved to {NewStart}, {RemainingLocked} chapters still locked",
                    privilege.NovelId, privilege.PrivilegeStartSequence.Value, privilege.CurrentLockedCount);
            }
            else if (privilege.CurrentLockedCount == 0)
            {
                logger.LogDebug(
                    "Novel {NovelId} has no locked chapters, skipping daily unlock (waiting for new chapter)",
                    privilege.NovelId);
            }
        }
        
        logger.LogInformation(
            "Daily unlock completed: {UnlockedCount} novels processed",
            unlockedCount);
    }

    private sealed class AlreadySubscribedException : Exception;
}
