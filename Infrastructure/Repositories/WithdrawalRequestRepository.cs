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
}
