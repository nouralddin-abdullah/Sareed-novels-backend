using Domain.Entities;

namespace Domain.Repositories;

public interface IReadingListNovelsRepository
{
    Task<ReadingListNovel?> GetAsync(Guid readingListId, Guid novelId);
    Task<(IEnumerable<Novel>, int)> GetNovelsInListAsync(Guid readingListId, int pageNumber, int pageSize);
    Task<bool> AddNovelAsync(ReadingListNovel readingListNovel);
    Task<bool> RemoveNovelAsync(Guid readingListId, Guid novelId);
    Task<bool> IsNovelInListAsync(Guid readingListId, Guid novelId);
    Task<int> GetNovelsCountAsync(Guid readingListId);
    Task<int> RemoveDeletedNovelsAsync(Guid readingListId);
}
