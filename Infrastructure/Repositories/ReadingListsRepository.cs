using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ReadingListsRepository(ApplicationDbContext dbContext) : IReadingListsRepository
{
    public async Task<ReadingList?> GetByIdAsync(Guid id)
    {
        return await dbContext.ReadingLists
            .Include(rl => rl.Owner)
            .FirstOrDefaultAsync(rl => rl.Id == id);
    }

    public async Task<ReadingList?> GetByIdWithNovelsAsync(Guid id)
    {
        var readingList = await dbContext.ReadingLists
            .Include(rl => rl.Owner)
            .Include(rl => rl.Novels)
                .ThenInclude(rln => rln.Novel)
                    .ThenInclude(n => n.Owner)
            .FirstOrDefaultAsync(rl => rl.Id == id);

        if (readingList != null)
        {
            readingList.Novels = readingList.Novels
                .Where(rln => !rln.Novel.IsDraft)
                .ToList();
        }

        return readingList;
    }

    public async Task<ReadingList?> GetByIdWithDetailsAsync(Guid id)
    {
        return await dbContext.ReadingLists
            .Include(rl => rl.Owner)
            .Include(rl => rl.Novels)
                .ThenInclude(rln => rln.Novel)
                    .ThenInclude(n => n.NovelGenres)
                        .ThenInclude(ng => ng.Genre)
            .AsSplitQuery()
            .FirstOrDefaultAsync(rl => rl.Id == id);
    }

    public async Task<(IEnumerable<ReadingList>, int)> GetUserReadingListsAsync(string userId, int pageNumber, int pageSize)
    {
        var query = dbContext.ReadingLists
            .Where(rl => rl.UserId == userId)
            .OrderByDescending(rl => rl.UpdatedAt);

        var totalCount = await query.CountAsync();
        
        var lists = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (lists, totalCount);
    }

    public async Task<(IEnumerable<ReadingList>, int)> GetUserReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize)
    {
        var query = dbContext.ReadingLists
            .Where(rl => rl.UserId == userId)
            .OrderByDescending(rl => rl.UpdatedAt);

        var totalCount = await query.CountAsync();
        
        var lists = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync();

        foreach (var list in lists)
        {
            await dbContext.Entry(list)
                .Collection(rl => rl.Novels)
                .Query()
                .OrderBy(rln => rln.OrderIndex)
                .Take(5)
                .Include(rln => rln.Novel)
                .Where(rln => !rln.Novel.IsDraft)
                .LoadAsync();
        }

        return (lists, totalCount);
    }

    public async Task<(IEnumerable<ReadingList>, int)> GetPublicReadingListsAsync(int pageNumber, int pageSize)
    {
        var query = dbContext.ReadingLists
            .Where(rl => rl.IsPublic)
            .Include(rl => rl.Owner)
            .OrderByDescending(rl => rl.FollowersCount)
            .ThenByDescending(rl => rl.UpdatedAt);

        var totalCount = await query.CountAsync();
        
        var lists = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (lists, totalCount);
    }

    public async Task<(IEnumerable<ReadingList>, int)> GetFollowedReadingListsAsync(string userId, int pageNumber, int pageSize)
    {
        var query = dbContext.ReadingListFollowers
            .Where(rlf => rlf.UserId == userId)
            .Select(rlf => rlf.ReadingList)
            .Include(rl => rl.Owner)
            .OrderByDescending(rl => rl.UpdatedAt);

        var totalCount = await query.CountAsync();
        
        var lists = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (lists, totalCount);
    }

    public async Task<(IEnumerable<ReadingList>, int)> GetFollowedReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize)
    {
        var followedListIds = await dbContext.ReadingListFollowers
            .Where(rlf => rlf.UserId == userId)
            .Select(rlf => rlf.ReadingListId)
            .ToListAsync();

        if (!followedListIds.Any())
        {
            return (Enumerable.Empty<ReadingList>(), 0);
        }

        var query = dbContext.ReadingLists
            .Where(rl => followedListIds.Contains(rl.Id))
            .OrderByDescending(rl => rl.UpdatedAt);

        var totalCount = followedListIds.Count;
        
        var lists = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync();

        foreach (var list in lists)
        {
            await dbContext.Entry(list)
                .Collection(rl => rl.Novels)
                .Query()
                .OrderBy(rln => rln.OrderIndex)
                .Take(5)
                .Include(rln => rln.Novel)
                .Where(rln => !rln.Novel.IsDraft)
                .LoadAsync();
        }

        return (lists, totalCount);
    }

    public async Task<bool> CreateAsync(ReadingList readingList)
    {
        dbContext.ReadingLists.Add(readingList);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UpdateAsync(ReadingList readingList)
    {
        readingList.UpdatedAt = DateTime.UtcNow;
        dbContext.ReadingLists.Update(readingList);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var readingList = await dbContext.ReadingLists.FindAsync(id);
        if (readingList == null) return false;

        dbContext.ReadingLists.Remove(readingList);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> IsNameTakenByUserAsync(string userId, string name, Guid? excludeListId = null)
    {
        var query = dbContext.ReadingLists
            .Where(rl => rl.UserId == userId && rl.Name.ToLower() == name.ToLower());

        if (excludeListId.HasValue)
        {
            query = query.Where(rl => rl.Id != excludeListId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<(IEnumerable<ReadingList>, int)> GetUserPublicReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize)
    {
        var query = dbContext.ReadingLists
            .Where(rl => rl.UserId == userId && rl.IsPublic)
            .OrderByDescending(rl => rl.UpdatedAt);

        var totalCount = await query.CountAsync();
        
        var lists = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync();

        foreach (var list in lists)
        {
            await dbContext.Entry(list)
                .Collection(rl => rl.Novels)
                .Query()
                .OrderBy(rln => rln.OrderIndex)
                .Take(5)
                .Include(rln => rln.Novel)
                .Where(rln => !rln.Novel.IsDraft)
                .LoadAsync();
        }

        return (lists, totalCount);
    }
}
