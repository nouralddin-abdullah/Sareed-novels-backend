using Domain.Constants;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class PlayPurchaseRepository(ApplicationDbContext dbContext) : IPlayPurchaseRepository
{
    public Task<PlayPurchase?> GetByTokenAsync(string purchaseToken) =>
        dbContext.PlayPurchases.AsNoTracking().SingleOrDefaultAsync(p => p.PurchaseToken == purchaseToken);

    public async Task<bool> TryAddAsync(PlayPurchase purchase)
    {
        dbContext.PlayPurchases.Add(purchase);
        try
        {
            // Inside a transaction EF saves behind a savepoint, so a duplicate leaves the transaction usable.
            await dbContext.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another request recorded this token first (it waited on that insert until it committed).
            dbContext.Entry(purchase).State = EntityState.Detached;
            return false;
        }
    }

    public Task<List<PlayPurchase>> GetConsumptionsDueAsync(DateTime now, int max) =>
        dbContext.PlayPurchases.AsNoTracking()
            .Where(p => p.NextConsumeAttemptAt <= now && p.ConsumedAt == null && p.Status == PlayPurchaseStatus.Credited)
            .OrderBy(p => p.NextConsumeAttemptAt)
            .Take(max)
            .ToListAsync();

    public Task MarkConsumedAsync(Guid id, DateTime consumedAt) =>
        dbContext.PlayPurchases
            .Where(p => p.Id == id && p.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ConsumedAt, consumedAt)
                .SetProperty(p => p.NextConsumeAttemptAt, (DateTime?)null)
                .SetProperty(p => p.LastConsumeError, (string?)null));

    public Task RecordConsumeFailureAsync(Guid id, string error, DateTime nextAttemptAt)
    {
        var message = error.Length > PlayPurchase.ConsumeErrorMaxLength ? error[..PlayPurchase.ConsumeErrorMaxLength] : error;
        return dbContext.PlayPurchases
            .Where(p => p.Id == id && p.ConsumedAt == null && p.Status == PlayPurchaseStatus.Credited)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ConsumeAttempts, p => p.ConsumeAttempts + 1)
                .SetProperty(p => p.LastConsumeError, message)
                .SetProperty(p => p.NextConsumeAttemptAt, nextAttemptAt));
    }

    public async Task<bool> TryMarkVoidedAsync(Guid id, DateTime? voidedAt, int? voidedReason, int? voidedSource) =>
        await dbContext.PlayPurchases
            .Where(p => p.Id == id && p.Status == PlayPurchaseStatus.Credited)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PlayPurchaseStatus.Voided)
                .SetProperty(p => p.VoidedAt, voidedAt)
                .SetProperty(p => p.VoidedReason, voidedReason)
                .SetProperty(p => p.VoidedSource, voidedSource)
                // Nothing is owed to Google for a voided purchase: stop the consume retries.
                .SetProperty(p => p.NextConsumeAttemptAt, (DateTime?)null)) == 1;

    public async Task<DateTime?> GetSyncCursorAsync(string name) =>
        await dbContext.PlaySyncCursors.AsNoTracking()
            .Where(c => c.Name == name)
            .Select(c => (DateTime?)c.SyncedUntil)
            .SingleOrDefaultAsync();

    public async Task AdvanceSyncCursorAsync(string name, DateTime syncedUntil, DateTime now)
    {
        // During an overlapped app-pool recycle two workers can sync at once; the cursor only ever moves forward.
        var updated = await dbContext.PlaySyncCursors
            .Where(c => c.Name == name)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.SyncedUntil, c => c.SyncedUntil < syncedUntil ? syncedUntil : c.SyncedUntil)
                .SetProperty(c => c.UpdatedAt, now));
        if (updated > 0)
        {
            return;
        }

        var cursor = new PlaySyncCursor { Name = name, SyncedUntil = syncedUntil, UpdatedAt = now };
        dbContext.PlaySyncCursors.Add(cursor);
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.Entry(cursor).State = EntityState.Detached;
            await AdvanceSyncCursorAsync(name, syncedUntil, now);
        }
    }
}
