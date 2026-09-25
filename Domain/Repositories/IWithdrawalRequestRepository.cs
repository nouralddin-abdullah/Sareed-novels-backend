using Domain.Entities;

namespace Domain.Repositories;

public interface IWithdrawalRequestRepository
{
    Task<WithdrawalRequest> CreateAsync(WithdrawalRequest request);
    Task<WithdrawalRequest?> GetByIdAsync(Guid id);
    Task<(IEnumerable<WithdrawalRequest>, int)> GetUserRequestsAsync(string userId, int pageNumber, int pageSize, string? status = null);
    Task<(IEnumerable<WithdrawalRequest>, int)> GetPendingRequestsAsync(int pageNumber, int pageSize);
    Task<bool> UpdateAsync(WithdrawalRequest request);

    /// <summary>
    /// Moves a Pending request to <paramref name="newStatus"/> in one conditional UPDATE. Returns false if the request
    /// is missing or no longer Pending, so a request can be approved or rejected only once, even concurrently.
    /// </summary>
    Task<bool> TryMarkProcessedAsync(Guid id, string newStatus, string processedBy, string? rejectionReason = null);
}
