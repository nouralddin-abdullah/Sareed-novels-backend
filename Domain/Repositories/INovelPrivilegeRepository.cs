using Domain.Entities;

namespace Domain.Repositories;

/// <summary>
/// A novel's early access (#94): its settings row (<see cref="NovelPrivilege"/>) and its chapters' locks
/// (<see cref="Chapter.EarlyAccessFrom"/>, <see cref="Chapter.EarlyAccessFreedAt"/>). Every write is one SQL statement
/// with its condition in it, never a write-back of a row read earlier, and a change of a novel's settings runs inside a
/// transaction holding the novel (<see cref="HoldAsync"/>), so two changes of one novel run one after the other.
/// </summary>
public interface INovelPrivilegeRepository
{
    /// <summary>The novel's early-access row, with the novel (for its author), as stored now; null when it was never set up.</summary>
    Task<NovelPrivilege?> GetByNovelIdAsync(Guid novelId);

    /// <summary>
    /// Holds the novel's early access until the current transaction ends: another change of its settings or locks waits
    /// for it (up to 20 seconds, then this throws). Call it first, inside a transaction.
    /// </summary>
    Task HoldAsync(Guid novelId);

    /// <summary>Inserts the row of a novel that enables early access for the first time; false when it has one already.</summary>
    Task<bool> CreateAsync(NovelPrivilege privilege);

    /// <summary>Turns early access on again with these settings, for a novel that has it off; false when it is on.</summary>
    Task<bool> TurnOnAsync(Guid novelId, decimal subscriptionCost, int? earlyAccessDays, bool subscribersOnly, DateTime now);

    /// <summary>Saves new settings of a novel that has early access on; false when it is off.</summary>
    Task<bool> UpdateSettingsAsync(Guid novelId, decimal subscriptionCost, int? earlyAccessDays, bool subscribersOnly, DateTime now);

    /// <summary>Turns early access off (its subscriptions stay); false when it was off already or never set up.</summary>
    Task<bool> TurnOffAsync(Guid novelId, DateTime now);

    /// <summary>The novel's published chapters in reading order, with their locks as stored.</summary>
    Task<List<PublishedChapterLock>> GetPublishedLocksAsync(Guid novelId);

    /// <summary>A chapter's lock as stored now, and whether it is published; null when the chapter is gone.</summary>
    Task<StoredChapterLock?> GetChapterLockAsync(Guid chapterId);

    /// <summary>
    /// The locks of enabling: every chapter of the novel loses the lock it had from an earlier time early access was on,
    /// then the published chapters from position <paramref name="fromSequence"/> on lock from <paramref name="now"/>.
    /// How many locked.
    /// </summary>
    Task<int> LockFromPositionAsync(Guid novelId, int fromSequence, DateTime now);

    /// <summary>
    /// Locks a chapter that just came out, from when it came out (<see cref="Chapter.PublishedAt"/>): only while the novel
    /// has early access on, the chapter is published, has no lock yet and wasn't freed, and its published position is past
    /// <paramref name="freeChapters"/>. True when it locked.
    /// </summary>
    Task<bool> LockCameOutAsync(Guid chapterId, int freeChapters);

    /// <summary>Frees a chapter for good at <paramref name="now"/>; false when it had no lock or was freed already.</summary>
    Task<bool> FreeAsync(Guid chapterId, DateTime now);

    /// <summary>Frees for good the published chapters of the novel before position <paramref name="beforeSequence"/> that have a lock.</summary>
    Task<int> FreeBeforeAsync(Guid novelId, int beforeSequence, DateTime now);

    /// <summary>
    /// Frees for good the chapters of the novel whose lock started at or before <paramref name="startedAtOrBefore"/>:
    /// the locks over under the days in force, so that no later change of the days locks them again.
    /// </summary>
    Task<int> FreezeEndedAsync(Guid novelId, DateTime startedAtOrBefore, DateTime now);
}

/// <summary>A published chapter of a novel and its lock as stored (#94): its published position, when its lock started, when it was freed.</summary>
public sealed record PublishedChapterLock(Guid ChapterId, int Sequence, DateTime? From, DateTime? FreedAt);

/// <summary>A chapter's lock as stored (#94), with its novel and whether it is published.</summary>
public sealed record StoredChapterLock(Guid NovelId, bool IsPublished, DateTime? From, DateTime? FreedAt);
