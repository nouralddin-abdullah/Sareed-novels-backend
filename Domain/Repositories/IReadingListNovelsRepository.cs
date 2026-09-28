using Domain.Entities;

namespace Domain.Repositories;

public interface IReadingListNovelsRepository
{
    Task<ReadingListNovel?> GetAsync(Guid readingListId, Guid novelId);
    Task<(IEnumerable<Novel>, int)> GetNovelsInListAsync(Guid readingListId, int pageNumber, int pageSize);
    /// <summary>Adds the novel unless the list has it (safe under concurrency); true when this call added it.</summary>
    Task<bool> AddNovelAsync(ReadingListNovel readingListNovel);
    /// <summary>Removes the novel if the list has it (safe under concurrency); true when this call removed it.</summary>
    Task<bool> RemoveNovelAsync(Guid readingListId, Guid novelId);
    Task<bool> IsNovelInListAsync(Guid readingListId, Guid novelId);
    Task<int> GetNovelsCountAsync(Guid readingListId);
    Task<int> RemoveDeletedNovelsAsync(Guid readingListId);
    /// <summary>The OrderIndex that puts a newly added novel at the end of the list.</summary>
    Task<int> GetNextOrderIndexAsync(Guid readingListId);
}
