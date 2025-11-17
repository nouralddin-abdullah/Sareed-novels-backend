using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class RechargeRequestRepository(ApplicationDbContext dbContext) : IRechargeRequestRepository
{
    public async Task<RechargeRequest> CreateAsync(RechargeRequest request)
    {
        dbContext.RechargeRequests.Add(request);
        await dbContext.SaveChangesAsync();
        return request;
    }

    public async Task<RechargeRequest?> GetByIdAsync(Guid id)
    {
        return await dbContext.RechargeRequests
            .Include(r => r.User)
            .Include(r => r.ProcessedByUser)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<(IEnumerable<RechargeRequest>, int)> GetUserRequestsAsync(string userId, int pageNumber, int pageSize, string? status = null)
    {
        var query = dbContext.RechargeRequests
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

    public async Task<(IEnumerable<RechargeRequest>, int)> GetPendingRequestsAsync(int pageNumber, int pageSize)
    {
        var query = dbContext.RechargeRequests
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

    public async Task<bool> UpdateAsync(RechargeRequest request)
    {
        dbContext.RechargeRequests.Update(request);
        return await dbContext.SaveChangesAsync() > 0;
    }
}
