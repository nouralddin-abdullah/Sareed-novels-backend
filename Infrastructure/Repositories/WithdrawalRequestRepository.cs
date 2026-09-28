using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class WithdrawalRequestRepository(ApplicationDbContext dbContext) : IWithdrawalRequestRepository
{
    public async Task<WithdrawalRequest> CreateAsync(WithdrawalRequest request)
    {
        dbContext.WithdrawalRequests.Add(request);
        await dbContext.SaveChangesAsync();
        return request;
    }

    public async Task<WithdrawalRequest?> GetByIdAsync(Guid id)
    {
        return await dbContext.WithdrawalRequests
            .Include(r => r.User)
            .Include(r => r.ProcessedByUser)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<(IEnumerable<WithdrawalRequest>, int)> GetUserRequestsAsync(string userId, int pageNumber, int pageSize, string? status = null)
    {
        var query = dbContext.WithdrawalRequests
            .Where(r => r.UserId == userId);

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(r => r.Status == status);
        }

        var totalCount = await query.CountAsync();

        var requests = await query
            .OrderByDescending(r => r.RequestedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (requests, totalCount);
    }

    public async Task<(IEnumerable<WithdrawalRequest>, int)> GetPendingRequestsAsync(int pageNumber, int pageSize)
    {
        var query = dbContext.WithdrawalRequests
            .Where(r => r.Status == Domain.Constants.RequestStatus.Pending)
            .Include(r => r.User);

        var totalCount = await query.CountAsync();

        var requests = await query
            .OrderBy(r => r.RequestedAt) // Oldest first for admin queue
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (requests, totalCount);
    }

    public async Task<bool> UpdateAsync(WithdrawalRequest request)
    {
        dbContext.WithdrawalRequests.Update(request);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<decimal> GetPendingPointsAsync(string userId) =>
        await dbContext.WithdrawalRequests.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status == Domain.Constants.RequestStatus.Pending)
            .SumAsync(r => (decimal)r.PointsRequested);

    public async Task<(string UserId, string Status, string? RejectionReason)?> GetStateAsync(Guid id)
    {
        var row = await dbContext.WithdrawalRequests.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.UserId, r.Status, r.RejectionReason })
            .SingleOrDefaultAsync();
        return row is null ? null : (row.UserId, row.Status, row.RejectionReason);
    }

    public async Task<bool> TryMarkProcessedAsync(Guid id, string newStatus, string processedBy, string? rejectionReason = null)
    {
        var updated = await dbContext.WithdrawalRequests
            .Where(r => r.Id == id && r.Status == Domain.Constants.RequestStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, newStatus)
                .SetProperty(r => r.ProcessedAt, DateTime.UtcNow)
                .SetProperty(r => r.ProcessedBy, processedBy)
                .SetProperty(r => r.RejectionReason, rejectionReason));
        return updated == 1;
    }
}
