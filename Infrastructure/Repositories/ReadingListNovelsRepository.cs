using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ReadingListNovelsRepository(ApplicationDbContext dbContext) : IReadingListNovelsRepository
{
    public async Task<bool> AddNovelAsync(ReadingListNovel readingListNovel)
    {
        // A concurrent duplicate is a no-op (the key lock), not a primary key violation (500).
        var n = readingListNovel;
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ReadingListNovels (ReadingListId, NovelId, AddedAt, OrderIndex)
            SELECT {n.ReadingListId}, {n.NovelId}, {n.AddedAt}, {n.OrderIndex}
            WHERE NOT EXISTS (SELECT 1 FROM ReadingListNovels WITH (UPDLOCK, HOLDLOCK)
                              WHERE ReadingListId = {n.ReadingListId} AND NovelId = {n.NovelId})
            """);
        return inserted == 1;
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

    public async Task<bool> RemoveNovelAsync(Guid readingListId, Guid novelId) =>
        // One statement: a concurrent removal deletes nothing instead of failing on the row the other one removed.
        await dbContext.ReadingListNovels
            .IgnoreQueryFilters()
            .Where(rln => rln.ReadingListId == readingListId && rln.NovelId == novelId)
            .ExecuteDeleteAsync() > 0;

    public async Task<int> GetNovelsCountAsync(Guid readingListId)
    {
        // Count only visible novels (global filter + IsDraft check)
        return await dbContext.ReadingListNovels
            .Where(rln => rln.ReadingListId == readingListId)
            .Select(rln => rln.Novel)
            .Where(n => !n.IsDraft)
            .CountAsync();
    }
    
    public async Task<int> GetNextOrderIndexAsync(Guid readingListId)
    {
        var last = await dbContext.ReadingListNovels
            .IgnoreQueryFilters() // hidden novels still hold their place
            .Where(rln => rln.ReadingListId == readingListId)
            .MaxAsync(rln => (int?)rln.OrderIndex);
        return last + 1 ?? 0;
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
