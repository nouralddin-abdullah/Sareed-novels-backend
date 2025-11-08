using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class SearchIndexOutboxRepository(ApplicationDbContext dbContext) : ISearchIndexOutboxRepository
{
    public async Task<bool> AddOutboxEntryAsync(SearchIndexOutbox entry)
    {
        dbContext.SearchIndexOutbox.Add(entry);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<List<SearchIndexOutbox>> GetPendingEntriesAsync(int batchSize = 100)
    {
        return await dbContext.SearchIndexOutbox
            .Where(e => !e.Processed && e.RetryCount < 5) // Max 5 retries
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync();
    }

    public async Task<bool> MarkAsProcessedAsync(Guid entryId)
    {
        var entry = await dbContext.SearchIndexOutbox.FindAsync(entryId);
        if (entry == null) return false;

        entry.Processed = true;
        entry.ProcessedAt = DateTime.UtcNow;
        entry.ErrorMessage = null;

        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> MarkAsFailedAsync(Guid entryId, string errorMessage)
    {
        var entry = await dbContext.SearchIndexOutbox.FindAsync(entryId);
        if (entry == null) return false;

        entry.ErrorMessage = errorMessage.Length > 2000 
            ? errorMessage.Substring(0, 2000) 
            : errorMessage;

        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> IncrementRetryCountAsync(Guid entryId)
    {
        var entry = await dbContext.SearchIndexOutbox.FindAsync(entryId);
        if (entry == null) return false;

        entry.RetryCount++;

        // If max retries reached, mark as failed
        if (entry.RetryCount >= 5)
        {
            entry.Processed = true;
            entry.ProcessedAt = DateTime.UtcNow;
            if (string.IsNullOrEmpty(entry.ErrorMessage))
            {
                entry.ErrorMessage = "Max retry count reached";
            }
        }

        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<int> GetPendingCountAsync()
    {
        return await dbContext.SearchIndexOutbox
            .Where(e => !e.Processed && e.RetryCount < 5)
            .CountAsync();
    }
}
