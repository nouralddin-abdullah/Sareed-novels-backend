using Domain.Entities;

namespace Domain.Repositories;

public interface IGiftTransactionRepository
{
    Task<GiftTransaction> CreateTransaction(GiftTransaction transaction);
    Task<(IEnumerable<GiftTransaction> transactions, int totalCount)> GetTransactionsByNovel(Guid novelId, int pageNumber, int pageSize);
    Task<(IEnumerable<GiftTransaction> transactions, int totalCount)> GetTransactionsBySender(string senderId, int pageNumber, int pageSize);
    Task<List<(string UserId, decimal TotalPoints, int TotalGifts)>> GetTopSupportersForNovel(Guid novelId, int topCount);
    Task<decimal> GetTotalPointsReceivedByNovel(Guid novelId);

    /// <summary>The messages of these gift records that have one (#31), by record id.</summary>
    Task<Dictionary<Guid, string>> GetMessagesAsync(IReadOnlyCollection<Guid> transactionIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// A moderator's removal of a gift's message (#31): the message becomes null and the gift stays. False when there was
    /// no message to remove (none, removed already, or no such gift).
    /// </summary>
    Task<bool> RemoveMessageAsync(Guid transactionId, CancellationToken cancellationToken = default);
}
