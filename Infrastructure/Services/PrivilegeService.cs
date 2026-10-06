using Application.Common;
using Application.Privileges;
using Application.Services;
using Application.Users.Commands.FollowUser;
using Application.Wallet;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Early access (#94). Its rules are <see cref="EarlyAccess"/>'s; its settings and its chapters' locks are written in SQL,
/// a change of a novel's settings inside a transaction holding the novel (<see cref="INovelPrivilegeRepository.HoldAsync"/>).
/// </summary>
public class PrivilegeService(
    ILogger<PrivilegeService> logger,
    INovelPrivilegeRepository privilegeRepository,
    IPrivilegeSubscriptionRepository subscriptionRepository,
    INovelsRepository novelsRepository,
    IChaptersRepository chaptersRepository,
    IWalletService walletService,
    ITransactionManager transactionManager,
    IServiceScopeFactory scopeFactory,
    TimeProvider time) : IPrivilegeService
{
    public const string NotEnabledMessage = "الوصول المبكر غير مفعّل لهذه الرواية";
    public const string ChapterNotLockedMessage = "هذا الفصل غير مقفل";
    public const string BothModesMessage = "اختر واحدًا: عدد أيام الوصول المبكر أو للمشتركين فقط";
    public const string InvalidDaysMessage = "عدد أيام الوصول المبكر يجب أن يكون من 1 إلى 30";
    public const string NoChangesMessage = "لم يتغير شيء في الإعدادات";
    public const string FirstChaptersFreeMessage =
        "تبقى الفصول العشرة الأولى مجانية للقرّاء، فالوصول المبكر يبدأ من الفصل 11 أو بعده.";

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ===== READING =====

    public async Task<bool> IsChapterLockedAsync(Guid chapterId, string? userId = null)
    {
        var chapter = await privilegeRepository.GetChapterLockAsync(chapterId);
        if (chapter is null)
        {
            return false;
        }

        var privilege = await privilegeRepository.GetByNovelIdAsync(chapter.NovelId);
        if (!EarlyAccess.IsLocked(new ChapterLock(chapter.IsPublished, chapter.From, chapter.FreedAt), EarlyAccessSettings.Of(privilege), Now))
        {
            return false;
        }

        // Locked for non-subscribers: its author and its subscribers still read it.
        return userId is null
               || (privilege!.Novel?.AuthorId != userId && !await subscriptionRepository.HasActiveSubscriptionAsync(chapter.NovelId, userId));
    }

    public async Task<EarlyAccessView> GetViewAsync(Guid novelId, string novelAuthorId, string? viewerId)
    {
        var settings = EarlyAccessSettings.Of(await privilegeRepository.GetByNovelIdAsync(novelId));
        var readsLockedChapters = viewerId is not null
                                  && (viewerId == novelAuthorId
                                      || (settings is { IsEnabled: true }
                                          && await subscriptionRepository.HasActiveSubscriptionAsync(novelId, viewerId)));
        return new EarlyAccessView(settings, readsLockedChapters, Now);
    }

    public async Task<NovelPrivilege?> GetPrivilegeConfigAsync(Guid novelId) =>
        await privilegeRepository.GetByNovelIdAsync(novelId);

    public async Task<EarlyAccessSummary> SummarizeAsync(Guid novelId, EarlyAccessSettings? settings) =>
        EarlyAccessSummary.Of(await privilegeRepository.GetPublishedLocksAsync(novelId), settings, Now);

    public async Task<bool> HasActiveSubscriptionAsync(Guid novelId, string userId) =>
        await subscriptionRepository.HasActiveSubscriptionAsync(novelId, userId);

    // ===== AUTHOR OPERATIONS =====

    public async Task<OperationResult> EnablePrivilegeAsync(Guid novelId, string authorId, decimal subscriptionCost,
        int? privilegeStartSequence = null, int? earlyAccessDays = null, bool? subscribersOnly = null)
    {
        if (await AuthorRefusal(novelId, authorId) is { } refusal)
        {
            return refusal;
        }

        // Days, or subscribers only; neither (the website before #94) is the default days.
        if (subscribersOnly == true && earlyAccessDays.HasValue)
        {
            return Fail("ValidationFailed", BothModesMessage);
        }
        if (earlyAccessDays is < EarlyAccess.MinDays or > EarlyAccess.MaxDays)
        {
            return Fail("InvalidEarlyAccessDays", InvalidDaysMessage);
        }
        var only = subscribersOnly == true;
        var days = only ? (int?)null : earlyAccessDays ?? EarlyAccess.DefaultDays;

        if (subscriptionCost is < EarlyAccess.MinCost or > EarlyAccess.MaxCost)
        {
            return Fail("InvalidSubscriptionCost", "سعر الاشتراك يجب أن يكون من 100 إلى 2000 نقطة");
        }
        if (privilegeStartSequence is <= EarlyAccess.FreeChapters)
        {
            return Fail("FirstChaptersMustStayFree", FirstChaptersFreeMessage);
        }

        var now = Now;
        return await transactionManager.InTransactionAsync(async () =>
        {
            await privilegeRepository.HoldAsync(novelId);
            var existing = await privilegeRepository.GetByNovelIdAsync(novelId);
            if (existing is { IsEnabled: true })
            {
                return Fail("PrivilegeAlreadyEnabled", "الوصول المبكر مفعّل لهذه الرواية بالفعل");
            }

            var published = await novelsRepository.GetPublishedChaptersCountAsync(novelId);
            if (EarlyAccess.DefaultStart(published) is not { } defaultStart)
            {
                return Fail("NotEnoughPublishedChapters",
                    $"يلزم 11 فصلًا منشورًا على الأقل لتفعيل الوصول المبكر (المنشور الآن: {published}). تبقى الفصول العشرة الأولى مجانية للقرّاء.");
            }

            var start = privilegeStartSequence ?? defaultStart;
            if (start > published)
            {
                return Fail("InvalidPrivilegeStart", $"رقم أول فصل مقفل يجب أن يكون من 11 إلى {published}");
            }
            var count = published - start + 1;
            if (count > EarlyAccess.MaxLockedWhenEnabled)
            {
                return Fail("TooManyLockedChapters",
                    $"البدء من الفصل {start} يقفل {ArabicCount.ChaptersObject(count)}، والحد الأقصى 20 فصلًا. ابدأ من الفصل {published - 19} أو بعده.");
            }

            // The first time, its row; again after it was turned off, the same row (its subscriptions are still there).
            var on = existing is null
                ? await privilegeRepository.CreateAsync(new NovelPrivilege
                {
                    Id = Guid.NewGuid(),
                    NovelId = novelId,
                    IsEnabled = true,
                    SubscriptionCost = subscriptionCost,
                    EarlyAccessDays = days,
                    SubscribersOnly = only,
                    CreatedAt = now
                })
                : await privilegeRepository.TurnOnAsync(novelId, subscriptionCost, days, only, now);
            if (!on)
            {
                return Fail("PrivilegeAlreadyEnabled", "الوصول المبكر مفعّل لهذه الرواية بالفعل");
            }

            var locked = await privilegeRepository.LockFromPositionAsync(novelId, start, now);
            logger.LogInformation(
                "Early access enabled for novel {NovelId} by {AuthorId}: chapters {Start}-{Published} locked ({Locked}), {Mode}, cost {Cost}",
                novelId, authorId, start, published, locked, only ? "subscribers only" : $"{days} days", subscriptionCost);

            var lasting = only
                ? "وتبقى مقفلة لغير المشتركين حتى تفتحها بنفسك"
                : $"ويُفتح كل فصل منها للجميع بعد {ArabicCount.DaysObject(days!.Value)} من قفله";
            return new OperationResult
            {
                Success = true,
                Message = $"تم تفعيل الوصول المبكر. الفصول من {start} إلى {published} مقفلة الآن ({ArabicCount.Chapters(count)})، {lasting}، وتبقى الفصول العشرة الأولى مجانية. سعر الاشتراك {Points.Format(subscriptionCost)} نقطة."
            };
        });
    }

    public async Task<OperationResult> UpdatePrivilegeConfigAsync(Guid novelId, string authorId, decimal? newSubscriptionCost = null,
        int? newPrivilegeStartSequence = null, int? earlyAccessDays = null, bool? subscribersOnly = null)
    {
        if (await AuthorRefusal(novelId, authorId) is { } refusal)
        {
            return refusal;
        }
        if (subscribersOnly == true && earlyAccessDays.HasValue)
        {
            return Fail("ValidationFailed", BothModesMessage);
        }
        if (earlyAccessDays is < EarlyAccess.MinDays or > EarlyAccess.MaxDays)
        {
            return Fail("InvalidEarlyAccessDays", InvalidDaysMessage);
        }
        if (newSubscriptionCost is < EarlyAccess.MinCost or > EarlyAccess.MaxCost)
        {
            return Fail("InvalidSubscriptionCost", "سعر الاشتراك يجب أن يكون من 100 إلى 2000 نقطة");
        }
        if (newPrivilegeStartSequence is <= EarlyAccess.FreeChapters)
        {
            return Fail("FirstChaptersMustStayFree", FirstChaptersFreeMessage);
        }

        var now = Now;
        return await transactionManager.InTransactionAsync(async () =>
        {
            await privilegeRepository.HoldAsync(novelId);
            var privilege = await privilegeRepository.GetByNovelIdAsync(novelId);
            if (privilege is not { IsEnabled: true })
            {
                return Fail("PrivilegeNotEnabled", NotEnabledMessage);
            }

            var settings = EarlyAccessSettings.Of(privilege)!.Value;
            var cost = newSubscriptionCost ?? privilege.SubscriptionCost;

            // The mode asked for: subscribers only, or days (switching from subscribers only without days takes the default).
            var (days, only) = subscribersOnly == true
                ? ((int?)null, true)
                : earlyAccessDays.HasValue || subscribersOnly == false
                    ? (earlyAccessDays ?? settings.Days ?? EarlyAccess.DefaultDays, false)
                    : (settings.Days, settings.SubscribersOnly);
            var modeChanged = days != settings.Days || only != settings.SubscribersOnly;

            // The website before #94 moves the first locked chapter forward: the locked chapters before it are freed.
            var startChanged = false;
            if (newPrivilegeStartSequence is { } newStart)
            {
                var summary = await SummarizeAsync(novelId, settings);
                var first = summary.FirstLockedSequence ?? summary.PublishedCount + 1;
                if (newStart > summary.PublishedCount)
                {
                    return Fail("InvalidPrivilegeStart", $"رقم أول فصل مقفل يجب أن يكون من 11 إلى {summary.PublishedCount}");
                }
                if (newStart < first)
                {
                    return Fail("PrivilegeStartCannotMoveBack",
                        $"لا يمكن إرجاع بداية الفصول المقفلة من الفصل {first} إلى الفصل {newStart}، فهذا يقفل فصولًا فُتحت للقرّاء من قبل. يمكن تقديمها فقط.");
                }
                startChanged = newStart > first;
            }

            if (cost == privilege.SubscriptionCost && !modeChanged && !startChanged)
            {
                return Fail("NoChanges", NoChangesMessage);
            }

            // New days apply to the chapters still locked, their end counted from their own lock's start, so a lock that is
            // over now is frozen first: no change of the days ever locks a chapter readers could already read.
            if (modeChanged && !settings.SubscribersOnly)
            {
                await privilegeRepository.FreezeEndedAsync(novelId, now.AddDays(-(settings.Days ?? EarlyAccess.DefaultDays)), now);
            }
            if (startChanged)
            {
                await privilegeRepository.FreeBeforeAsync(novelId, newPrivilegeStartSequence!.Value, now);
            }
            await privilegeRepository.UpdateSettingsAsync(novelId, cost, days, only, now);

            logger.LogInformation(
                "Early access of novel {NovelId} changed by {AuthorId}: cost {Cost}, {Mode}, first locked moved to {Start}",
                novelId, authorId, cost, only ? "subscribers only" : $"{days} days", newPrivilegeStartSequence);
            return new OperationResult { Success = true, Message = "حُفظت إعدادات الوصول المبكر" };
        });
    }

    public async Task<OperationResult> ManuallyUnlockChapterAsync(Guid novelId, Guid chapterId, string authorId)
    {
        var chapter = await chaptersRepository.GetChapterById(chapterId);
        if (chapter is null || chapter.NovelId != novelId)
        {
            return Fail("ChapterNotFound", "الفصل غير موجود");
        }
        if (await AuthorRefusal(novelId, authorId) is { } refusal)
        {
            return refusal;
        }

        var now = Now;
        return await transactionManager.InTransactionAsync(async () =>
        {
            await privilegeRepository.HoldAsync(novelId);
            var privilege = await privilegeRepository.GetByNovelIdAsync(novelId);
            if (privilege is not { IsEnabled: true })
            {
                return Fail("PrivilegeNotEnabled", NotEnabledMessage);
            }

            // That chapter only, as stored now: freed for good, so no later change locks it again.
            var settings = EarlyAccessSettings.Of(privilege);
            var stored = await privilegeRepository.GetChapterLockAsync(chapterId);
            if (stored is null
                || !EarlyAccess.IsLocked(new ChapterLock(stored.IsPublished, stored.From, stored.FreedAt), settings, now)
                || !await privilegeRepository.FreeAsync(chapterId, now))
            {
                return Fail("ChapterNotLocked", ChapterNotLockedMessage);
            }

            var stillLocked = (await SummarizeAsync(novelId, settings)).LockedCount;
            logger.LogInformation("Author {AuthorId} freed chapter {ChapterId} of novel {NovelId}: {Locked} still locked",
                authorId, chapterId, novelId, stillLocked);
            return new OperationResult { Success = true, Message = $"فُتح الفصل للجميع. الفصول المقفلة الآن: {stillLocked}" };
        });
    }

    public async Task<OperationResult> DisablePrivilegeAsync(Guid novelId, string authorId)
    {
        if (await AuthorRefusal(novelId, authorId) is { } refusal)
        {
            return refusal;
        }

        var now = Now;
        return await transactionManager.InTransactionAsync(async () =>
        {
            await privilegeRepository.HoldAsync(novelId);
            if (!await privilegeRepository.TurnOffAsync(novelId, now))
            {
                return Fail("PrivilegeNotEnabled", NotEnabledMessage);
            }

            logger.LogInformation("Early access disabled for novel {NovelId} by {AuthorId}", novelId, authorId);
            return new OperationResult
            {
                Success = true,
                Message = "أُوقف الوصول المبكر: كل الفصول متاحة للجميع الآن، والفصول الجديدة لا تُقفل. ويبقى المشتركون مشتركين إن فعّلته من جديد."
            };
        });
    }

    /// <summary>NovelNotFound or NotOwner unless <paramref name="authorId"/> wrote the novel.</summary>
    private async Task<OperationResult?> AuthorRefusal(Guid novelId, string authorId)
    {
        var novel = await novelsRepository.GetOne(novelId);
        return novel is null ? Fail("NovelNotFound", "الرواية غير موجودة")
            : novel.AuthorId != authorId ? Fail("NotOwner", "هذا الإجراء متاح لكاتب الرواية فقط")
            : null;
    }

    private static OperationResult Fail(string code, string message) => new() { Success = false, Code = code, Message = message };

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

    // ===== CHAPTERS =====

    public async Task<bool> OnChapterCameOutAsync(Guid chapterId)
    {
        var locked = await privilegeRepository.LockCameOutAsync(chapterId, EarlyAccess.FreeChapters);
        if (locked)
        {
            logger.LogInformation("Chapter {ChapterId} came out in early access", chapterId);
        }
        return locked;
    }

    private sealed class AlreadySubscribedException : Exception;
}
