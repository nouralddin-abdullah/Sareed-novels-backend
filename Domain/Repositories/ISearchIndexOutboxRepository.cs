using Domain.Entities;

namespace Domain.Repositories;

public interface ISearchIndexOutboxRepository
{
    Task<bool> AddOutboxEntryAsync(SearchIndexOutbox entry);
    Task<List<SearchIndexOutbox>> GetPendingEntriesAsync(int batchSize = 100);
    Task<bool> MarkAsProcessedAsync(Guid entryId);
    Task<bool> MarkAsFailedAsync(Guid entryId, string errorMessage);
    Task<bool> IncrementRetryCountAsync(Guid entryId);
    Task<int> GetPendingCountAsync();
}
