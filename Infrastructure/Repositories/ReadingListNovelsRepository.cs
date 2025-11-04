using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ReadingListNovelsRepository(ApplicationDbContext dbContext) : IReadingListNovelsRepository
{
    public async Task<bool> AddNovelAsync(ReadingListNovel readingListNovel)
    {
        dbContext.ReadingListNovels.Add(readingListNovel);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<ReadingListNovel?> GetAsync(Guid readingListId, Guid novelId)
    {
        return await dbContext.ReadingListNovels
            .IgnoreQueryFilters() // Check existence regardless of soft delete
            .FirstOrDefaultAsync(rln => rln.ReadingListId == readingListId && rln.NovelId == novelId);
    }

    public async Task<(IEnumerable<Novel>, int)> GetNovelsInListAsync(Guid readingListId, int pageNumber, int pageSize)
    {
        // The global query filter on Novel automatically excludes IsDeleted = true
        // We just need to add the IsDraft filter
        var query = dbContext.ReadingListNovels
            .Where(rln => rln.ReadingListId == readingListId)
            .OrderBy(rln => rln.OrderIndex)
            .ThenByDescending(rln => rln.AddedAt)
            .Select(rln => rln.Novel)
            .Include(n => n.Owner)
            .Where(n => !n.IsDraft); // Global filter handles IsDeleted, we handle IsDraft

        var totalCount = await query.CountAsync();
        
        var novels = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (novels, totalCount);
    }

    public async Task<bool> IsNovelInListAsync(Guid readingListId, Guid novelId)
    {
        return await dbContext.ReadingListNovels
            .IgnoreQueryFilters() // Check raw existence
            .AnyAsync(rln => rln.ReadingListId == readingListId && rln.NovelId == novelId);
    }

    public async Task<bool> RemoveNovelAsync(Guid readingListId, Guid novelId)
    {
        var readingListNovel = await GetAsync(readingListId, novelId);
        if (readingListNovel == null) return false;

        dbContext.ReadingListNovels.Remove(readingListNovel);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<int> GetNovelsCountAsync(Guid readingListId)
    {
        // Count only visible novels (global filter + IsDraft check)
        return await dbContext.ReadingListNovels
            .Where(rln => rln.ReadingListId == readingListId)
            .Select(rln => rln.Novel)
            .Where(n => !n.IsDraft)
            .CountAsync();
    }
    
    public async Task<int> RemoveDeletedNovelsAsync(Guid readingListId)
    {
        // Use IgnoreQueryFilters to find deleted novels
        var deletedEntries = await dbContext.ReadingListNovels
            .Include(rln => rln.Novel)
            .IgnoreQueryFilters()
            .Where(rln => rln.ReadingListId == readingListId && rln.Novel.IsDeleted)
            .ToListAsync();

        if (deletedEntries.Any())
        {
            dbContext.ReadingListNovels.RemoveRange(deletedEntries);
            await dbContext.SaveChangesAsync();
            return deletedEntries.Count;
        }

        return 0;
    }
}
