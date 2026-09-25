using Domain.Entities;
using Domain.ReadingLists;

namespace Domain.Repositories;

public interface IReadingListsRepository
{
    Task<ReadingList?> GetByIdAsync(Guid id);
    Task<ReadingList?> GetByIdWithNovelsAsync(Guid id);
    Task<ReadingList?> GetByIdWithDetailsAsync(Guid id);
    Task<(IEnumerable<ReadingList>, int)> GetUserReadingListsAsync(string userId, int pageNumber, int pageSize);
    Task<(IReadOnlyList<ReadingListSummary>, int)> GetUserReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize);
    Task<(IReadOnlyList<ReadingListSummary>, int)> GetUserPublicReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize);
    Task<(IEnumerable<ReadingList>, int)> GetPublicReadingListsAsync(int pageNumber, int pageSize);
    Task<(IEnumerable<ReadingList>, int)> GetFollowedReadingListsAsync(string userId, int pageNumber, int pageSize);
    /// <summary>Lists the user follows that are still public; a list its owner made private drops out.</summary>
    Task<(IReadOnlyList<ReadingListSummary>, int)> GetFollowedReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize);
    Task<bool> CreateAsync(ReadingList readingList);
    Task<bool> UpdateAsync(ReadingList readingList);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> IsNameTakenByUserAsync(string userId, string name, Guid? excludeListId = null);
    /// <summary>Atomically adds <paramref name="delta"/> to NovelsCount (never below 0) and touches UpdatedAt.</summary>
    Task AdjustNovelsCountAsync(Guid readingListId, int delta);
    /// <summary>Atomically adds <paramref name="delta"/> to FollowersCount (never below 0).</summary>
    Task AdjustFollowersCountAsync(Guid readingListId, int delta);
}
