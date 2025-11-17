using Domain.Entities;

namespace Domain.Repositories;

public interface IPointTransactionRepository
{
    Task<PointTransaction> CreateAsync(PointTransaction transaction);
    Task<(IEnumerable<PointTransaction>, int)> GetUserTransactionsAsync(string userId, int pageNumber, int pageSize);
}
