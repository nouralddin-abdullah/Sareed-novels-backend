using Domain.Entities;

namespace Domain.Repositories;

public interface IChaptersRepository
{
    Task<bool> CreateChapter(Chapter chapter);
    Task<Chapter?> GetChapterById(Guid chapterId);
    Task<Chapter?> GetChapterBySlug(string slug);
    /// <summary>
    /// Saves the author's edit of a chapter loaded with <see cref="GetChapterById"/>, in one transaction: the edit of the
    /// chapter's text it is part of (<see cref="IChapterParagraphsRepository.BeginEditAsync"/>, committed with it), or
    /// one of its own. When the chapter came out (<see cref="Chapter.PublishedAt"/>) is stored once, and never from the
    /// loaded copy: this save stores it only while the chapter has none, so a save made from an older copy can neither
    /// erase nor move it, and of two saves that publish a new chapter at once, only one is its first publish. The
    /// comment and view counters and the paragraph count aren't written either; they move with their own atomic
    /// updates. Nor is the published sequence, which only the recalculation after a publish or unpublish writes (a
    /// chapter of the same novel published meanwhile, by hand or on schedule, renumbers this one too), and the word
    /// count only when the save changed it (#77: the startup backfill counts older chapters meanwhile).
    /// <see cref="Chapter.Revision"/> is written only when the edit moved it, and only over the revision it was loaded
    /// at (#75): if the stored one isn't that any more, nothing is saved and
    /// <see cref="Exceptions.ChapterChangedException"/> is thrown.
    /// </summary>
    Task<ChapterSave> UpdateChapter(Chapter chapter);

    /// <summary>Reads the chapter again into the tracked <paramref name="chapter"/>, as it is now; false when it is gone.</summary>
    Task<bool> ReloadAsync(Chapter chapter);

    /// <summary>
    /// The drafts whose scheduled time (<see cref="Chapter.PublishAt"/>, #77) has come, at or before
    /// <paramref name="now"/>: at most <paramref name="max"/> ids, the soonest due first and a novel's chapters in reading
    /// order; of one novel when it is given by id or slug. Chapters of deleted novels are left out.
    /// </summary>
    Task<List<Guid>> GetDueChapterIdsAsync(DateTime now, int max, Guid? novelId = null, string? novelSlug = null);

    Task<int> GetNextChapterIndex(Guid novelId);

    /// <summary>Up to <paramref name="take"/> chapter ids after <paramref name="after"/> (all chapters, in id order).</summary>
    Task<List<Guid>> GetChapterIdsAsync(Guid? after, int take);

    /// <summary>
    /// Chapters that still hold text in the legacy <c>Chapters.Content</c> column (from before the paragraphs, and from
    /// edits before #74, which copied the request into it): nothing reads it.
    /// </summary>
    Task<LegacyChapterContent> CountLegacyContentAsync();
    Task<bool> DeleteChapter(Chapter chapter);
    Task<IEnumerable<Chapter>> GetChaptersAuthorView(Guid novelId);
    Task<IEnumerable<Chapter>> GetChaptersReaderView(Guid novelId);
    Task<bool> ReorderChapters(Guid novelId, List<Guid> orderedChapterIds);
    Task<string?> GetNextChapterSlug(Guid novelId, int currentChapterIndex);
    /// <summary>The current titles of these chapters, by id; chapters that no longer exist are left out.</summary>
    Task<Dictionary<Guid, string>> GetTitlesAsync(IReadOnlyCollection<Guid> chapterIds);
}

/// <summary>
/// Chapters with text in the legacy <c>Chapters.Content</c> column (<see cref="IChaptersRepository.CountLegacyContentAsync"/>),
/// and how many of those have no paragraphs, so that column is their only text.
/// </summary>
public readonly record struct LegacyChapterContent(int Chapters, int WithoutParagraphs);

/// <summary>
/// What saving the author's edit of a chapter did (<see cref="IChaptersRepository.UpdateChapter"/>), or publishing it on
/// schedule (#77, through the same save).
/// </summary>
/// <param name="Saved">The chapter was saved.</param>
/// <param name="CameOut">
/// This save stored when the chapter came out: it published a chapter that had never been published (#39). Only then
/// is the chapter new to readers: they are told, and the novel's last update moves.
/// </param>
public readonly record struct ChapterSave(bool Saved, bool CameOut);
