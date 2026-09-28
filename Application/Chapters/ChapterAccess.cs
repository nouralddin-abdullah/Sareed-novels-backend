using Domain.Constants;
using Domain.Entities;

namespace Application.Chapters;

/// <summary>Who may open a chapter in the reader (GET .../chapter/{id}), and count a read of it (POST .../view).</summary>
internal static class ChapterAccess
{
    /// <summary>
    /// Readers only get published chapters of published novels, through the novel they belong to; the novel's author
    /// can also preview their drafts. Anything else is answered as a missing chapter.
    /// </summary>
    public static bool IsReadable(Novel novel, Chapter chapter, bool isAuthor) =>
        chapter.NovelId == novel.Id && (isAuthor || (!novel.IsDraft && chapter.Status == ChapterStatuses.Published));
}
