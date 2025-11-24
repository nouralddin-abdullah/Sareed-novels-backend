using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class NovelPrivilegeRepository(ApplicationDbContext dbContext) : INovelPrivilegeRepository
{
    public async Task<NovelPrivilege?> GetByNovelIdAsync(Guid novelId)
    {
        return await dbContext.NovelPrivileges
            .Include(p => p.Novel)
            .FirstOrDefaultAsync(p => p.NovelId == novelId);
    }

    public async Task<NovelPrivilege> CreateAsync(NovelPrivilege privilege)
    {
        dbContext.NovelPrivileges.Add(privilege);
        await dbContext.SaveChangesAsync();
        return privilege;
    }

    public async Task<bool> UpdateAsync(NovelPrivilege privilege)
    {
        privilege.UpdatedAt = DateTime.UtcNow;
        dbContext.NovelPrivileges.Update(privilege);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteAsync(Guid privilegeId)
    {
        var privilege = await dbContext.NovelPrivileges.FindAsync(privilegeId);
        if (privilege == null)
            return false;

        dbContext.NovelPrivileges.Remove(privilege);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<List<NovelPrivilege>> GetAllEnabledPrivilegesAsync()
    {
        return await dbContext.NovelPrivileges
            .Where(p => p.IsEnabled && p.CurrentLockedCount > 0)
            .ToListAsync();
    }

    public async Task<bool> ExistsAsync(Guid novelId)
    {
        return await dbContext.NovelPrivileges
            .AnyAsync(p => p.NovelId == novelId);
    }
}
