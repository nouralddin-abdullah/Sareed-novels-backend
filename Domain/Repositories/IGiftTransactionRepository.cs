using Domain.Entities;

namespace Domain.Repositories;

public interface IGiftTransactionRepository
{
    Task<GiftTransaction> CreateTransaction(GiftTransaction transaction);
    Task<(IEnumerable<GiftTransaction> transactions, int totalCount)> GetTransactionsByNovel(Guid novelId, int pageNumber, int pageSize);
    Task<(IEnumerable<GiftTransaction> transactions, int totalCount)> GetTransactionsBySender(string senderId, int pageNumber, int pageSize);
    Task<List<(string UserId, decimal TotalPoints, int TotalGifts)>> GetTopSupportersForNovel(Guid novelId, int topCount);
    Task<decimal> GetTotalPointsReceivedByNovel(Guid novelId);
}
