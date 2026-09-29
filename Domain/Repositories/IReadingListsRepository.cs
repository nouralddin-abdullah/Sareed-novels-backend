using Domain.Entities;
using Domain.ReadingLists;

namespace Domain.Repositories;

public interface IReadingListsRepository
{
    Task<ReadingList?> GetByIdAsync(Guid id);
    Task<ReadingList?> GetByIdWithNovelsAsync(Guid id);
    Task<ReadingList?> GetByIdWithDetailsAsync(Guid id);
    Task<(IEnumerable<ReadingList>, int)> GetUserReadingListsAsync(string userId, int pageNumber, int pageSize);
    /// <summary>
    /// A page of the user's lists. With <paramref name="containsNovelId"/>, each says whether it has that novel
    /// (<see cref="ReadingListSummary.ContainsNovel"/>), from the same query.
    /// </summary>
    Task<(IReadOnlyList<ReadingListSummary>, int)> GetUserReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize,
        Guid? containsNovelId = null);
    /// <summary>One list as the list pages summarize them (visible-novel count and preview), or null if it doesn't exist.</summary>
    Task<ReadingListSummary?> GetSummaryAsync(Guid readingListId);
    Task<(IReadOnlyList<ReadingListSummary>, int)> GetUserPublicReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize);
    Task<(IEnumerable<ReadingList>, int)> GetPublicReadingListsAsync(int pageNumber, int pageSize);
    Task<(IEnumerable<ReadingList>, int)> GetFollowedReadingListsAsync(string userId, int pageNumber, int pageSize);
    /// <summary>
    /// Lists the user follows that are still public; a list its owner made private drops out, and so does one whose
    /// owner blocked the user.
    /// </summary>
    Task<(IReadOnlyList<ReadingListSummary>, int)> GetFollowedReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize);
    Task<bool> CreateAsync(ReadingList readingList);
    /// <summary>
    /// Saves the changes made to a list read with <see cref="GetByIdAsync"/> (tracked) and touches UpdatedAt: only the
    /// columns that changed are written, never the counters as they were read.
    /// </summary>
    Task<bool> UpdateAsync(ReadingList readingList);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> IsNameTakenByUserAsync(string userId, string name, Guid? excludeListId = null);
    /// <summary>The current names of these lists, by id; lists that no longer exist are left out.</summary>
    Task<Dictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> readingListIds);
    /// <summary>Atomically adds <paramref name="delta"/> to NovelsCount (never below 0) and touches UpdatedAt.</summary>
    Task AdjustNovelsCountAsync(Guid readingListId, int delta);
    /// <summary>Atomically adds <paramref name="delta"/> to FollowersCount (never below 0).</summary>
    Task AdjustFollowersCountAsync(Guid readingListId, int delta);
}
