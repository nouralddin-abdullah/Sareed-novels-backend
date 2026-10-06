using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;

namespace Application.Privileges;

/// <summary>A novel's early-access settings as a lock is read with (#94); none when early access was never set up.</summary>
/// <param name="IsEnabled">Early access is on.</param>
/// <param name="Days">How many days a lock lasts; null when <paramref name="SubscribersOnly"/>.</param>
/// <param name="SubscribersOnly">Locks last until the author frees the chapter.</param>
public readonly record struct EarlyAccessSettings(bool IsEnabled, int? Days, bool SubscribersOnly)
{
    public static EarlyAccessSettings? Of(NovelPrivilege? privilege) =>
        privilege is null ? null : new EarlyAccessSettings(privilege.IsEnabled, privilege.EarlyAccessDays, privilege.SubscribersOnly);
}

/// <summary>A chapter's early-access lock as stored (#94): whether it is published, when its lock started, when it was freed.</summary>
public readonly record struct ChapterLock(bool IsPublished, DateTime? From, DateTime? FreedAt)
{
    public static ChapterLock Of(Chapter chapter) =>
        new(chapter.Status == ChapterStatuses.Published, chapter.EarlyAccessFrom, chapter.EarlyAccessFreedAt);
}

/// <summary>
/// The rules of early access («الوصول المبكر», #94), the one place every reader-facing check reads them from. A chapter
/// is locked by its own lock, stored on it, never by its position among the published chapters: it is locked while
/// early access is on, the chapter is published, its lock started and it wasn't freed, and the novel is subscribers-only
/// or its lock started less than the novel's days ago. Locked, it is open to the novel's author and its subscribers only.
/// </summary>
public static class EarlyAccess
{
    /// <summary>The first published chapters always stay free: enabling never locks them, nor does a chapter coming out among them.</summary>
    public const int FreeChapters = 10;

    /// <summary>The most chapters enabling locks at once; chapters that come out later lock whatever their number.</summary>
    public const int MaxLockedWhenEnabled = 20;

    public const int MinDays = 1;
    public const int MaxDays = 30;

    /// <summary>The days of a request that names neither days nor subscribers only (the website before #94), and of the novels from before it.</summary>
    public const int DefaultDays = 7;

    public const decimal MinCost = 100;
    public const decimal MaxCost = 2000;

    /// <summary>A subscription's price: whole points (#96), from <see cref="MinCost"/> to <see cref="MaxCost"/>.</summary>
    public static bool IsValidCost(decimal cost) => cost is >= MinCost and <= MaxCost && decimal.Truncate(cost) == cost;

    /// <summary>Whether <paramref name="chapter"/> is locked for non-subscribers at <paramref name="now"/>.</summary>
    public static bool IsLocked(ChapterLock chapter, EarlyAccessSettings? settings, DateTime now) =>
        settings is { IsEnabled: true } on
        && chapter is { IsPublished: true, From: not null, FreedAt: null }
        && (on.SubscribersOnly || EndOf(chapter.From.Value, on) > now);

    /// <summary>
    /// When <paramref name="chapter"/>'s lock ends, for everyone: null when it isn't locked at <paramref name="now"/>, or
    /// when its lock has no end (subscribers only).
    /// </summary>
    public static DateTime? UnlocksAt(ChapterLock chapter, EarlyAccessSettings? settings, DateTime now) =>
        IsLocked(chapter, settings, now) && !settings!.Value.SubscribersOnly ? EndOf(chapter.From!.Value, settings.Value) : null;

    /// <summary>The end of a lock that started at <paramref name="from"/>, on a novel that counts days (UTC, sent with "Z").</summary>
    public static DateTime EndOf(DateTime from, EarlyAccessSettings settings) =>
        DateTime.SpecifyKind(from, DateTimeKind.Utc).AddDays(settings.Days ?? DefaultDays);

    /// <summary>
    /// Whether a chapter coming out at published position <paramref name="publishedSequence"/> locks: past the
    /// <see cref="FreeChapters"/> first ones (whether early access is on is checked with the lock).
    /// </summary>
    public static bool LocksWhenItComesOut(int publishedSequence) => publishedSequence > FreeChapters;

    /// <summary>
    /// The published position enabling locks from when the author names none: the last
    /// min(<see cref="MaxLockedWhenEnabled"/>, published - <see cref="FreeChapters"/>) chapters; null when there are no
    /// more than <see cref="FreeChapters"/>.
    /// </summary>
    public static int? DefaultStart(int publishedCount) =>
        publishedCount > FreeChapters ? publishedCount - Math.Min(MaxLockedWhenEnabled, publishedCount - FreeChapters) + 1 : null;
}

/// <summary>
/// What a novel's published chapters' locks come to at a moment (#94), for GET privilege: how many are locked for
/// non-subscribers, the earliest end among them (null when none ends by itself), the published position of the first
/// locked one (null when none is), and how many chapters are published.
/// </summary>
public readonly record struct EarlyAccessSummary(int LockedCount, DateTime? NextUnlockAt, int? FirstLockedSequence, int PublishedCount)
{
    public static EarlyAccessSummary Of(IReadOnlyList<PublishedChapterLock> chapters, EarlyAccessSettings? settings, DateTime now)
    {
        var locked = chapters
            .Select(chapter => (chapter.Sequence, Lock: new ChapterLock(true, chapter.From, chapter.FreedAt)))
            .Where(chapter => EarlyAccess.IsLocked(chapter.Lock, settings, now))
            .ToList();
        return new EarlyAccessSummary(
            locked.Count,
            locked.Select(chapter => EarlyAccess.UnlocksAt(chapter.Lock, settings, now)).Min(),
            locked.Count > 0 ? locked.Min(chapter => chapter.Sequence) : null,
            chapters.Count);
    }
}

/// <summary>
/// Early access for one viewer of one novel's chapters (#94), for a list: the novel's settings once, whether the viewer
/// reads past locks (the novel's author or a subscriber), and the moment the list is read.
/// </summary>
public sealed class EarlyAccessView(EarlyAccessSettings? settings, bool readsLockedChapters, DateTime now)
{
    /// <summary>Whether the chapter is locked for non-subscribers: what the author's own lists show.</summary>
    public bool IsLocked(Chapter chapter) => EarlyAccess.IsLocked(ChapterLock.Of(chapter), settings, now);

    /// <summary>When the chapter's lock ends for everyone (null when it isn't locked, or never ends by itself).</summary>
    public DateTime? UnlocksAt(Chapter chapter) => EarlyAccess.UnlocksAt(ChapterLock.Of(chapter), settings, now);

    /// <summary>
    /// When the chapter's lock started (UTC, #96), while it is locked for non-subscribers; null otherwise. New days end a
    /// still-locked chapter this many days after it.
    /// </summary>
    public DateTime? LockedAt(Chapter chapter) =>
        IsLocked(chapter) ? DateTime.SpecifyKind(chapter.EarlyAccessFrom!.Value, DateTimeKind.Utc) : null;

    /// <summary>Whether the chapter is locked for this viewer: never for the novel's author or a subscriber.</summary>
    public bool IsLockedForViewer(Chapter chapter) => !readsLockedChapters && IsLocked(chapter);

    /// <summary>When the chapter opens for this viewer: null when it is open to them already.</summary>
    public DateTime? UnlocksAtForViewer(Chapter chapter) => readsLockedChapters ? null : UnlocksAt(chapter);
}
