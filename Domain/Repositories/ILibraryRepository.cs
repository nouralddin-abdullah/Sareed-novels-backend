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
    /// Records that the user is now at <paramref name="chapterId"/> in the novel, creating the row on first read.
    /// Safe under concurrent calls. Returns true when the novel was newly added to the user's library.
    /// </summary>
    Task<bool> SaveProgressAsync(string userId, Guid novelId, Guid chapterId, int chapterNumber, DateTime readAt);

    Task<List<string>> GetUsersWithNovelInLibrary(Guid novelId);
}
