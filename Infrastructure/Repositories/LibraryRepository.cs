using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class LibraryRepository(ApplicationDbContext dbContext) : ILibraryRepository
{
    public async Task<(IEnumerable<UserNovelProgress>, int)> GetUserReadingProgressAsync(string userId, int pageNumber, int pageSize)
    {
        var query = dbContext.UserNovelProgress
            .Where(unp => unp.UserId == userId)
            .Include(unp => unp.Novel)
                .ThenInclude(n => n.Owner)
            .Include(unp => unp.Novel)
                .ThenInclude(n => n.Chapters.Where(c => c.Status == "Published"))
            .Include(unp => unp.LastReadChapter)
            .OrderByDescending(unp => unp.LastReadAt);

        var totalCount = await query.CountAsync();

        var progress = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync();

        return (progress, totalCount);
    }

    public async Task<UserNovelProgress?> GetProgressAsync(string userId, Guid novelId)
    {
        return await dbContext.UserNovelProgress
            .FirstOrDefaultAsync(unp => unp.UserId == userId && unp.NovelId == novelId);
    }

    public async Task<bool> TrackProgressAsync(UserNovelProgress progress)
    {
        dbContext.UserNovelProgress.Add(progress);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UpdateProgressAsync(UserNovelProgress progress)
    {
        dbContext.UserNovelProgress.Update(progress);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<List<string>> GetUsersWithNovelInLibrary(Guid novelId)
    {
        return await dbContext.UserNovelProgress
            .Where(unp => unp.NovelId == novelId)
            .Select(unp => unp.UserId)
            .Distinct()
            .ToListAsync();
    }
}
