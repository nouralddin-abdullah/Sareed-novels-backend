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

    public async Task<Novel?> GetOneBySlug(string slug)
    {
        var novel = await dbContext.Novels
            .Include(n=>n.Owner)
            .FirstOrDefaultAsync(novel => novel.Slug == slug);
        return novel;
    }

    public async Task<(IEnumerable<Novel?>, int)> GetWorks(string userId, int PageNumber, int PageSize)
    {
        var userWork = dbContext.Novels.Where(n => n.AuthorId == userId).AsQueryable();
        var totalCount = await userWork.CountAsync();
        if (PageNumber > 0 && PageSize > 0)
        {
            userWork = userWork.OrderBy(f => f.CreatedAt).Skip(PageSize * (PageNumber - 1)).Take(PageSize);
        }
        var userWorkList = await userWork.ToListAsync();
        return (userWorkList, totalCount);
    }

    public async Task<bool> UpdateOne(Novel novel)
    {
        dbContext.Novels.Update(novel);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }
}
