using Application.Privileges;
using Application.Users.Commands.FollowUser;
using Domain.Entities;

namespace Application.Services;

/// <summary>
/// A novel's early access («الوصول المبكر», #94): its settings, its chapters' locks, and its subscriptions. Who a locked
/// chapter is closed to is the rule of <see cref="EarlyAccess"/>; everything that shows a chapter's text, or counts a
/// read of it, asks here.
/// </summary>
public interface IPrivilegeService
{
    // ===== READING =====

    /// <summary>
    /// Whether the chapter is locked for <paramref name="userId"/> (null when signed out) now: locked for non-subscribers,
    /// and the viewer is neither the novel's author nor a subscriber.
    /// </summary>
    Task<bool> IsChapterLockedAsync(Guid chapterId, string? userId = null);

    /// <summary>
    /// Early access for <paramref name="viewerId"/> over the chapters of a novel by <paramref name="novelAuthorId"/>, read
    /// now: for a list of its chapters, the novel's settings once and whether the viewer reads past locks.
    /// </summary>
    Task<EarlyAccessView> GetViewAsync(Guid novelId, string novelAuthorId, string? viewerId);

    /// <summary>The novel's early-access row as stored; null when it was never set up.</summary>
    Task<NovelPrivilege?> GetPrivilegeConfigAsync(Guid novelId);

    /// <summary>What the novel's published chapters' locks come to now.</summary>
    Task<EarlyAccessSummary> SummarizeAsync(Guid novelId, EarlyAccessSettings? settings);

    /// <summary>Whether the member has an active subscription to the novel's early access.</summary>
    Task<bool> HasActiveSubscriptionAsync(Guid novelId, string userId);

    // ===== AUTHOR OPERATIONS =====

    /// <summary>
    /// Turns early access on, the first time or again after it was turned off: locks the published chapters from
    /// <paramref name="privilegeStartSequence"/> on (by default the last min(20, published - 10)), from now. A chapter
    /// stays locked <paramref name="earlyAccessDays"/> days (1-30; 7 when neither is sent), or for good with
    /// <paramref name="subscribersOnly"/>; not both.
    /// </summary>
    Task<OperationResult> EnablePrivilegeAsync(Guid novelId, string authorId, decimal subscriptionCost, int? privilegeStartSequence = null,
        int? earlyAccessDays = null, bool? subscribersOnly = null);

    /// <summary>
    /// Changes what is sent: the cost (for new subscribers), the days or subscribers only (for chapters that come out from
    /// now, and for those still locked, their end counted from their own lock's start; a lock already over stays over),
    /// and the position of the first locked chapter, forward only (the website before #94: frees the locked chapters before it).
    /// </summary>
    Task<OperationResult> UpdatePrivilegeConfigAsync(Guid novelId, string authorId, decimal? newSubscriptionCost = null,
        int? newPrivilegeStartSequence = null, int? earlyAccessDays = null, bool? subscribersOnly = null);

    /// <summary>Frees one locked chapter for everyone, for good.</summary>
    Task<OperationResult> ManuallyUnlockChapterAsync(Guid novelId, Guid chapterId, string authorId);

    /// <summary>Turns early access off: every chapter is open to everyone, new chapters don't lock, subscriptions stay.</summary>
    Task<OperationResult> DisablePrivilegeAsync(Guid novelId, string authorId);

    // ===== READER OPERATIONS =====

    /// <summary>
    /// User subscribes to a novel's privilege.
    /// Deducts points from wallet.
    /// A subscription is a permanent unlock: it can't be cancelled (the owner's rule, #17).
    /// </summary>
    Task<OperationResult> SubscribeToPrivilegeAsync(
        Guid novelId,
        string userId);

    // ===== CHAPTERS =====

    /// <summary>
    /// A chapter came out (published for the first time, by hand, created published or on schedule): it locks from when
    /// it came out while the novel has early access on, unless it is among the first 10 published chapters. Publishing it
    /// again, unpublishing, reordering or deleting chapters never locks or frees one. True when it locked.
    /// </summary>
    Task<bool> OnChapterCameOutAsync(Guid chapterId);
}
