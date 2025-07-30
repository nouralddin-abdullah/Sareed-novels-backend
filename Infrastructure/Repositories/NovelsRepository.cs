using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class NovelsRepository(ApplicationDbContext dbContext) : INovelsRepository
{
    public async Task<bool> CreateNovel(Novel novel)
    {
        await dbContext.Novels.AddAsync(novel);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }

    public async Task<Novel?> GetOne(Guid novelId)
    {
        var novel = await dbContext.Novels
            .Include(n=> n.Owner)
            .FirstOrDefaultAsync(novel => novel.Id == novelId);
        return novel;
    }

    public async Task<bool> UpdateOne(Novel novel)
    {
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }
}
