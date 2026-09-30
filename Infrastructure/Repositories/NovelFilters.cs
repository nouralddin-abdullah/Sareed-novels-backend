using Domain.Constants;
using Domain.Entities;

namespace Infrastructure.Repositories;

/// <summary>Which novels a list shows readers, as a filter in the list's own query.</summary>
internal static class NovelFilters
{
    /// <summary>
    /// Novels a reader can open and read: not a draft, with at least one published chapter (deleted novels are already
    /// left out by the query filter). The reader opens only published chapters of a novel that isn't a draft
    /// (Application.Chapters.ChapterAccess), so a novel without one has nothing to read. New arrivals, recommendations
    /// and a member's works with withChapters=true list only these; the rankings and the sitemap apply the same rule to
    /// the published chapters they load.
    /// </summary>
    public static IQueryable<Novel> Readable(this IQueryable<Novel> novels) =>
        novels.Where(n => !n.IsDraft && n.Chapters.Any(c => c.Status == ChapterStatuses.Published));
}
