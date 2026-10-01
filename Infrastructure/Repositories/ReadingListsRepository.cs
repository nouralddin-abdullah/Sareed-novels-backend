using Domain.Entities;
using Domain.ReadingLists;
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
                    .ThenInclude(n => n.Owner)
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

    public Task<(IReadOnlyList<ReadingListSummary>, int)> GetUserReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize,
        Guid? containsNovelId = null) =>
        PageWithPreviews(dbContext.ReadingLists.Where(rl => rl.UserId == userId), pageNumber, pageSize, containsNovelId);

    public async Task<ReadingListSummary?> GetSummaryAsync(Guid readingListId)
    {
        var (lists, _) = await PageWithPreviews(dbContext.ReadingLists.Where(rl => rl.Id == readingListId), 1, 1);
        return lists.SingleOrDefault();
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

    public Task<(IReadOnlyList<ReadingListSummary>, int)> GetFollowedReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize) =>
        PageWithPreviews(
            dbContext.ReadingLists.Where(rl => rl.IsPublic
                && rl.Followers.Any(f => f.UserId == userId)
                // A list whose owner blocked the user doesn't exist for them (#25); the follow stays for an unblock.
                && !dbContext.UserBlocks.Any(b => b.BlockerId == rl.UserId && b.BlockedId == userId)),
            pageNumber,
            pageSize);

    public async Task<bool> CreateAsync(ReadingList readingList)
    {
        dbContext.ReadingLists.Add(readingList);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UpdateAsync(ReadingList readingList)
    {
        readingList.UpdatedAt = DateTime.UtcNow;
        // Change tracking writes only the columns the caller changed. DbSet.Update marked every column modified, so an
        // edit wrote NovelsCount and FollowersCount back as they were when the list was read, undoing the adds,
        // removals and follows made meanwhile (a picture upload makes that window seconds long).
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var readingList = await dbContext.ReadingLists.FindAsync(id);
        if (readingList == null) return false;

        dbContext.ReadingLists.Remove(readingList);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<Dictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> readingListIds) =>
        readingListIds.Count == 0
            ? []
            : await dbContext.ReadingLists
                .AsNoTracking()
                .Where(rl => readingListIds.Contains(rl.Id))
                .Select(rl => new { rl.Id, rl.Name })
                .ToDictionaryAsync(rl => rl.Id, rl => rl.Name);

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

    public Task<(IReadOnlyList<ReadingListSummary>, int)> GetUserPublicReadingListsWithPreviewAsync(string userId, int pageNumber, int pageSize) =>
        PageWithPreviews(dbContext.ReadingLists.Where(rl => rl.UserId == userId && rl.IsPublic), pageNumber, pageSize);

    public Task AdjustNovelsCountAsync(Guid readingListId, int delta) =>
        dbContext.ReadingLists
            .Where(rl => rl.Id == readingListId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rl => rl.NovelsCount, rl => rl.NovelsCount + delta < 0 ? 0 : rl.NovelsCount + delta)
                .SetProperty(rl => rl.UpdatedAt, DateTime.UtcNow));

    public Task AdjustFollowersCountAsync(Guid readingListId, int delta) =>
        dbContext.ReadingLists
            .Where(rl => rl.Id == readingListId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rl => rl.FollowersCount, rl => rl.FollowersCount + delta < 0 ? 0 : rl.FollowersCount + delta));

    private const int PreviewSize = 5;

    /// <summary>
    /// One page of lists, most recently updated first, each with its visible-novel count and first few visible novels,
    /// in the same few queries whatever the page size. Draft novels are skipped here; the Novel query filter skips deleted ones.
    /// With <paramref name="containsNovelId"/>, each list also says whether it has that novel: one EXISTS in the page's
    /// query, not a query per list.
    /// </summary>
    private async Task<(IReadOnlyList<ReadingListSummary>, int)> PageWithPreviews(IQueryable<ReadingList> query, int pageNumber, int pageSize,
        Guid? containsNovelId = null)
    {
        var totalCount = await query.CountAsync();

        var page = await query
            .AsNoTracking()
            .OrderByDescending(rl => rl.UpdatedAt)
            .ThenBy(rl => rl.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(rl => new
            {
                List = rl,
                VisibleNovelsCount = rl.Novels.Count(rln => !rln.Novel.IsDraft),
                // The list's row for the novel, whatever the novel's state, as adding it (AlreadyInList) and removing it
                // see it. Without a novel to ask about, SQL Server gets a constant instead.
                ContainsNovel = containsNovelId != null && rl.Novels.Any(rln => rln.NovelId == containsNovelId),
                Preview = rl.Novels
                    .Where(rln => !rln.Novel.IsDraft)
                    .OrderBy(rln => rln.OrderIndex)
                    .ThenBy(rln => rln.AddedAt)
                    .Take(PreviewSize)
                    .Select(rln => new NovelPreview(rln.Novel.Id, rln.Novel.Slug, rln.Novel.CoverImageUrl, rln.Novel.Title))
                    .ToList()
            })
            .AsSplitQuery()
            .ToListAsync();

        return (page.Select(p => new ReadingListSummary(p.List, p.VisibleNovelsCount, p.Preview,
            containsNovelId is null ? null : p.ContainsNovel)).ToList(), totalCount);
    }
}
