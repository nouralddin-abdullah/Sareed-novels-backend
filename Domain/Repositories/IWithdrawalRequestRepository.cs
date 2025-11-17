using Domain.Entities;

namespace Domain.Repositories;

public interface IWithdrawalRequestRepository
{
    Task<WithdrawalRequest> CreateAsync(WithdrawalRequest request);
    Task<WithdrawalRequest?> GetByIdAsync(Guid id);
    Task<(IEnumerable<WithdrawalRequest>, int)> GetUserRequestsAsync(string userId, int pageNumber, int pageSize, string? status = null);
    Task<(IEnumerable<WithdrawalRequest>, int)> GetPendingRequestsAsync(int pageNumber, int pageSize);
    Task<bool> UpdateAsync(WithdrawalRequest request);
}
