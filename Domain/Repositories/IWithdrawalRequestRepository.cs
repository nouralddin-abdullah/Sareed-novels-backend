using Domain.Entities;

namespace Domain.Repositories;

public interface IWithdrawalRequestRepository
{
    Task<WithdrawalRequest> CreateAsync(WithdrawalRequest request);
    Task<WithdrawalRequest?> GetByIdAsync(Guid id);
    Task<(IEnumerable<WithdrawalRequest>, int)> GetUserRequestsAsync(string userId, int pageNumber, int pageSize, string? status = null);
    Task<(IEnumerable<WithdrawalRequest>, int)> GetPendingRequestsAsync(int pageNumber, int pageSize);
    Task<bool> UpdateAsync(WithdrawalRequest request);

    /// <summary>Points of the user's withdrawal requests still pending (reserved, not deducted until approval).</summary>
    Task<decimal> GetPendingPointsAsync(string userId);

    /// <summary>Whose the request is and where it stands, as the database holds it now (never a tracked copy); null if
    /// there is no such request.</summary>
    Task<(string UserId, string Status, string? RejectionReason)?> GetStateAsync(Guid id);

    /// <summary>
    /// Moves a Pending request to <paramref name="newStatus"/> in one conditional UPDATE. Returns false if the request
    /// is missing or no longer Pending, so a request can be approved or rejected only once, even concurrently.
    /// </summary>
    Task<bool> TryMarkProcessedAsync(Guid id, string newStatus, string processedBy, string? rejectionReason = null);
}
