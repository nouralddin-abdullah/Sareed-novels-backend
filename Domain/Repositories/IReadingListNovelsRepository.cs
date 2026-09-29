using Domain.Entities;
using Domain.ReadingLists;

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

    /// <summary>
    /// Puts the list's novels in the order of <paramref name="orderedNovelIds"/>, which must be exactly the novels readers
    /// can open on it (<see cref="ReadingListOrder.Apply"/>), and stores it as OrderIndex 0, 1, 2... Checked and written in
    /// one transaction with the list and its novels locked, so a concurrent reorder, add or removal can't slip in between.
    /// </summary>
    Task<ReadingListReorderResult> ReorderAsync(Guid readingListId, IReadOnlyList<Guid> orderedNovelIds, CancellationToken cancellationToken = default);
}
