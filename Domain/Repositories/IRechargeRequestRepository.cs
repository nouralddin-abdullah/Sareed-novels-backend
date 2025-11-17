using Domain.Entities;

namespace Domain.Repositories;

public interface IRechargeRequestRepository
{
    Task<RechargeRequest> CreateAsync(RechargeRequest request);
    Task<RechargeRequest?> GetByIdAsync(Guid id);
    Task<(IEnumerable<RechargeRequest>, int)> GetUserRequestsAsync(string userId, int pageNumber, int pageSize, string? status = null);
    Task<(IEnumerable<RechargeRequest>, int)> GetPendingRequestsAsync(int pageNumber, int pageSize);
    Task<bool> UpdateAsync(RechargeRequest request);
}
