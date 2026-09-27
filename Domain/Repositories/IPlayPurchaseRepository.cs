using Domain.Entities;

namespace Domain.Repositories;

public interface IPlayPurchaseRepository
{
    /// <summary>The purchase recorded for this token, read fresh from the database (not tracked).</summary>
    Task<PlayPurchase?> GetByTokenAsync(string purchaseToken);

    /// <summary>
    /// Inserts the purchase. Returns false and saves nothing if its token is already recorded: the unique index decides
    /// between concurrent requests for the same token, and the loser reads the winner's row.
    /// </summary>
    Task<bool> TryAddAsync(PlayPurchase purchase);

    /// <summary>Credited purchases not yet consumed on Google Play whose next attempt is due, oldest due first.</summary>
    Task<List<PlayPurchase>> GetConsumptionsDueAsync(DateTime now, int max);

    /// <summary>Records that the purchase is consumed; nothing more is owed to Google for it.</summary>
    Task MarkConsumedAsync(Guid id, DateTime consumedAt);

    /// <summary>Counts a failed consume attempt and schedules the next one.</summary>
    Task RecordConsumeFailureAsync(Guid id, string error, DateTime nextAttemptAt);

    /// <summary>
    /// Moves a Credited purchase to Voided in one conditional UPDATE. Returns false if it wasn't Credited any more, so a
    /// void reported twice (even at the same time) takes the points back once. Call inside the transaction that debits.
    /// </summary>
    Task<bool> TryMarkVoidedAsync(Guid id, DateTime? voidedAt, int? voidedReason, int? voidedSource);

    Task<DateTime?> GetSyncCursorAsync(string name);

    /// <summary>Moves the cursor to <paramref name="syncedUntil"/>; never moves it back.</summary>
    Task AdvanceSyncCursorAsync(string name, DateTime syncedUntil, DateTime now);
}
