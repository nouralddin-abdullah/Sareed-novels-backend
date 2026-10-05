using Domain.Entities;

namespace Domain.Repositories;

public interface IChaptersRepository
{
    Task<bool> CreateChapter(Chapter chapter);
    Task<Chapter?> GetChapterById(Guid chapterId);
    Task<Chapter?> GetChapterBySlug(string slug);
    /// <summary>
    /// Saves the author's edit of a chapter loaded with <see cref="GetChapterById"/>, in one transaction. When the
    /// chapter came out (<see cref="Chapter.PublishedAt"/>) is stored once, and never from the loaded copy: this save
    /// stores it only while the chapter has none, so a save made from an older copy can neither erase nor move it, and
    /// of two saves that publish a new chapter at once, only one is its first publish. The comment and view counters
    /// aren't written either; they move with their own atomic updates.
    /// The status is stored only by a save that sets one (<paramref name="withStatus"/>; one that doesn't keeps the stored
    /// status), and only where it differs from the stored one, so of two saves (or a save and the schedule,
    /// <see cref="PublishDueAsync"/>) changing it at once, one does (<see cref="ChapterSave.StatusChanged"/>); publishing
    /// clears the schedule. The schedule (<see cref="Chapter.PublishAt"/>) is stored only when the save changes it, and
    /// only on a draft; the published sequence never (the recalculation after a publish keeps it); the word count only
    /// when it changed (#77). Afterwards the tracked chapter is the stored one.
    /// </summary>
    Task<ChapterSave> UpdateChapter(Chapter chapter, bool withStatus = true);

    /// <summary>
    /// Drafts whose <see cref="Chapter.PublishAt"/> has come (at or before <paramref name="now"/>), soonest first, at
    /// most <paramref name="max"/>; of one novel when it is given by id or slug. Chapters of deleted novels are left out.
    /// </summary>
    Task<List<DueChapter>> GetDueChaptersAsync(DateTime now, int max, Guid? novelId = null, string? novelSlug = null);

    /// <summary>
    /// Publishes a chapter on schedule (#77), in one transaction, only while it is still a draft whose
    /// <see cref="Chapter.PublishAt"/> has come: its status, its schedule cleared, and, if it never came out, when it
    /// did (<paramref name="now"/>), as <see cref="UpdateChapter"/> stores a publish. Of two runs, or a run and the
    /// author's own publish, at the same moment, one publishes it (<see cref="ChapterSave.StatusChanged"/>).
    /// </summary>
    Task<ChapterSave> PublishDueAsync(Guid chapterId, DateTime now);
    Task<int> GetNextChapterIndex(Guid novelId);
    Task<bool> DeleteChapter(Chapter chapter);
    Task<IEnumerable<Chapter>> GetChaptersAuthorView(Guid novelId);
    Task<IEnumerable<Chapter>> GetChaptersReaderView(Guid novelId);
    Task<bool> ReorderChapters(Guid novelId, List<Guid> orderedChapterIds);
    Task<string?> GetNextChapterSlug(Guid novelId, int currentChapterIndex);
    /// <summary>The current titles of these chapters, by id; chapters that no longer exist are left out.</summary>
    Task<Dictionary<Guid, string>> GetTitlesAsync(IReadOnlyCollection<Guid> chapterIds);
}

/// <summary>
/// What saving the author's edit of a chapter did (<see cref="IChaptersRepository.UpdateChapter"/>), or publishing it on
/// schedule (<see cref="IChaptersRepository.PublishDueAsync"/>).
/// </summary>
/// <param name="Saved">The chapter was saved.</param>
/// <param name="CameOut">
/// This save stored when the chapter came out: it published a chapter that had never been published (#39). Only then
/// is the chapter new to readers: they are told, and the novel's last update moves.
/// </param>
/// <param name="StatusChanged">
/// This save changed the stored status (#77): the chapter was published or unpublished by it, and not by another save
/// or the schedule at the same moment. Only then do the chapter's sequences, the novel's chapter count and the
/// privilege window follow.
/// </param>
public readonly record struct ChapterSave(bool Saved, bool CameOut, bool StatusChanged);

/// <summary>A draft whose scheduled publish time has come (<see cref="IChaptersRepository.GetDueChaptersAsync"/>).</summary>
public sealed record DueChapter(Guid Id, Guid NovelId, string Slug, string Title);
