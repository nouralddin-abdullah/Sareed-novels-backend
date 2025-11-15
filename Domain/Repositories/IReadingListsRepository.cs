using Domain.Entities;

namespace Domain.Repositories;

public interface IReadingListsRepository
{
    Task<ReadingList?> GetByIdAsync(Guid id);
    Task<ReadingList?> GetByIdWithNovelsAsync(Guid id);
    Task<ReadingList?> GetByIdWithDetailsAsync(Guid id);
    Task<(IEnumerable<ReadingList>, int)> GetUserReadingListsAsync(string userId, int pageNumber, int pageSize);
    Task<(IEnumerable<ReadingList>, int)> GetUserReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize);
    Task<(IEnumerable<ReadingList>, int)> GetUserPublicReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize);
    Task<(IEnumerable<ReadingList>, int)> GetPublicReadingListsAsync(int pageNumber, int pageSize);
    Task<(IEnumerable<ReadingList>, int)> GetFollowedReadingListsAsync(string userId, int pageNumber, int pageSize);
    Task<(IEnumerable<ReadingList>, int)> GetFollowedReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize);
    Task<bool> CreateAsync(ReadingList readingList);
    Task<bool> UpdateAsync(ReadingList readingList);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> IsNameTakenByUserAsync(string userId, string name, Guid? excludeListId = null);
}
