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
    /// </summary>
    Task<ChapterSave> UpdateChapter(Chapter chapter);
    Task<int> GetNextChapterIndex(Guid novelId);
    Task<bool> DeleteChapter(Chapter chapter);
    Task<IEnumerable<Chapter>> GetChaptersAuthorView(Guid novelId);
    Task<IEnumerable<Chapter>> GetChaptersReaderView(Guid novelId);
    Task<bool> ReorderChapters(Guid novelId, List<Guid> orderedChapterIds);
    Task<string?> GetNextChapterSlug(Guid novelId, int currentChapterIndex);
    /// <summary>The current titles of these chapters, by id; chapters that no longer exist are left out.</summary>
    Task<Dictionary<Guid, string>> GetTitlesAsync(IReadOnlyCollection<Guid> chapterIds);
}

/// <summary>What saving the author's edit of a chapter did (<see cref="IChaptersRepository.UpdateChapter"/>).</summary>
/// <param name="Saved">The chapter was saved.</param>
/// <param name="CameOut">
/// This save stored when the chapter came out: it published a chapter that had never been published (#39). Only then
/// is the chapter new to readers: they are told, and the novel's last update moves.
/// </param>
public readonly record struct ChapterSave(bool Saved, bool CameOut);
