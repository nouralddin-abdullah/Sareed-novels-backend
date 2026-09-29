using Domain.Library;

namespace Domain.Repositories;

public interface ILibraryRepository
{
    /// <summary>
    /// The novels a user has reading progress in, most recently read first. Draft and deleted novels are left out
    /// (readers can't open them), and <c>TotalCount</c> counts the same rows as the pages.
    /// </summary>
    Task<(IReadOnlyList<LibraryEntry> Entries, int TotalCount)> GetUserLibraryAsync(string userId, int pageNumber, int pageSize);

    /// <summary>The user's library entry for one novel, or null when they have none (or the novel is hidden).</summary>
    Task<LibraryEntry?> GetLibraryEntryAsync(string userId, Guid novelId);

    /// <summary>
    /// Records that the user is now at <paramref name="chapterId"/> in the novel, creating the row on first read (with
    /// new-chapter notifications on). Safe under concurrent calls. Returns true when the novel was newly added to the
    /// user's library.
    /// </summary>
    Task<bool> SaveProgressAsync(string userId, Guid novelId, Guid chapterId, int chapterNumber, DateTime readAt);

    /// <summary>
    /// Takes the novel out of the user's library: deletes their progress row and its count in their library, whatever
    /// the novel's state. Safe under concurrent calls. Returns false when there was no row (nothing changed).
    /// </summary>
    Task<bool> RemoveFromLibraryAsync(string userId, Guid novelId);

    /// <summary>
    /// Turns the novel's new-chapter notifications on or off for the user. Returns false when the novel isn't in their
    /// library (no progress row); setting the value it already has is still true.
    /// </summary>
    Task<bool> SetNewChapterNotificationsAsync(string userId, Guid novelId, bool notify);

    /// <summary>
    /// Who a new chapter of the novel notifies (in the app and by push): the users with the novel in their library,
    /// except those who muted it (<c>NotifyNewChapters</c> off).
    /// </summary>
    Task<List<string>> GetUsersWithNovelInLibrary(Guid novelId);
}
