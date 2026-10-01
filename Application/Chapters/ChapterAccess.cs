using Application.Services;
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

    /// <summary>
    /// Whether the reader (GetChapterReaderHandler) shows <paramref name="chapter"/>'s text to
    /// <paramref name="viewerId"/> (null when signed out): the novel's author always, drafts too; anyone else only a
    /// chapter <see cref="IsReadable"/> for them that early access doesn't lock for them
    /// (<see cref="IsUnlockedForAsync"/>). What quotes a chapter's text elsewhere shows it only then: the comment
    /// context's paragraphExcerpt (GET /api/notifications/comment/{id}).
    /// </summary>
    public static async Task<bool> ShowsTextAsync(IPrivilegeService privileges, Novel novel, Chapter chapter,
        string? viewerId) =>
        IsReadable(novel, chapter, IsAuthor(novel.AuthorId, viewerId))
        && await IsUnlockedForAsync(privileges, novel.AuthorId, chapter.Id, viewerId);

    /// <summary>
    /// For a chapter readers can open (<see cref="IsReadable"/>: published, in a published novel), whether its text is
    /// open to <paramref name="viewerId"/>: always to the novel's author (<paramref name="novelAuthorId"/>); to anyone
    /// else unless the privilege system locks it for them (early access, which a subscription opens). The rest of
    /// <see cref="ShowsTextAsync"/>, for callers whose chapters are all readable, as a member's comment list's are
    /// (#60).
    /// </summary>
    public static async Task<bool> IsUnlockedForAsync(IPrivilegeService privileges, string novelAuthorId, Guid chapterId,
        string? viewerId) =>
        IsAuthor(novelAuthorId, viewerId) || !await privileges.IsChapterLockedAsync(chapterId, viewerId);

    private static bool IsAuthor(string novelAuthorId, string? viewerId) => viewerId != null && viewerId == novelAuthorId;
}
