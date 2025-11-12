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

    public async Task<(IEnumerable<Novel>, int)> GetLatestNovels(int pageSize, int pageNumber)
    {
        var query = dbContext.Novels
        .Where(n => n.IsEligibleForRanking)
        .Include(n => n.NovelGenres)
            .ThenInclude(ng => ng.Genre)
        .Include(n => n.Owner)
        .OrderByDescending(n => n.CreatedAt); // Real-time ordering by creation date

        var totalCount = await query.CountAsync();

        var novels = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (novels, totalCount);
    }

    public async Task<Novel?> GetOne(Guid novelId)
    {
        var novel = await dbContext.Novels
            .Include(n=> n.Owner)
            .Include(n => n.NovelGenres)
                .ThenInclude(ng => ng.Genre)
            .FirstOrDefaultAsync(novel => novel.Id == novelId);
        return novel;
    }

    public async Task<Novel?> GetOneBySlug(string slug)
    {
        var novel = await dbContext.Novels
            .Where(n => !n.IsDraft)
            .Include(n=>n.Owner)
            .Include(n => n.NovelGenres)
                .ThenInclude(ng => ng.Genre)
            .FirstOrDefaultAsync(novel => novel.Slug == slug);
        return novel;
    }


    public async Task<(IEnumerable<Novel?>, int)> GetWorks(string userId, int PageNumber, int PageSize)
    {
        var userWork = dbContext.Novels
            .Where(n => n.AuthorId == userId)
            .Include(n => n.NovelGenres)
                .ThenInclude(ng => ng.Genre)
            .AsQueryable();
        var totalCount = await userWork.CountAsync();
        if (PageNumber > 0 && PageSize > 0)
        {
            userWork = userWork.OrderBy(f => f.CreatedAt).Skip(PageSize * (PageNumber - 1)).Take(PageSize);
        }
        var userWorkList = await userWork.ToListAsync();
        return (userWorkList, totalCount);
    }

    public async Task<(IEnumerable<Novel>, int)> GetUserPublishedWorks(string userId, int pageNumber, int pageSize)
    {
        var query = dbContext.Novels
            .AsNoTracking()
            .Where(n => n.AuthorId == userId && !n.IsDraft && !n.IsDeleted)
            .Include(n => n.NovelGenres)
                .ThenInclude(ng => ng.Genre)
            .Include(n => n.Owner);

        var totalCount = await query.CountAsync();

        var novels = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (novels, totalCount);
    }

    public async Task<(IEnumerable<Novel>, int)> GetAllNovelsBasicAsync(int pageNumber, int pageSize)
    {
        var query = dbContext.Novels
            .AsNoTracking()
            .Where(n => !n.IsDraft && !n.IsDeleted)
            .OrderByDescending(n => n.CreatedAt);

        var totalCount = await query.CountAsync();

        var novels = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (novels, totalCount);
    }

    public async Task<bool> UpdateOne(Novel novel)
    {
        dbContext.Novels.Update(novel);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }

    public async Task<int> GetPublishedChaptersCountAsync(Guid novelId)
    {
        return await dbContext.Chapters
            .Where(c => c.NovelId == novelId && c.Status == "Published")
            .CountAsync();
    }

    public async Task<int> RecalculatePublishedSequencesAsync(Guid novelId)
    {
        var allChapters = await dbContext.Chapters
            .Where(c => c.NovelId == novelId)
            .OrderBy(c => c.ChapterIndex)
            .ToListAsync();

        var publishedChapters = allChapters
            .Where(c => c.Status == "Published")
            .ToList();

        var unpublishedChapters = allChapters
            .Where(c => c.Status != "Published")
            .ToList();

        // Assign sequences to published chapters
        int sequence = 1;
        foreach (var chapter in publishedChapters)
        {
            chapter.PublishedChapterSequence = sequence++;
        }

        // Clear sequences from unpublished chapters
        foreach (var chapter in unpublishedChapters)
        {
            chapter.PublishedChapterSequence = null;
        }

        await dbContext.SaveChangesAsync();
        return publishedChapters.Count;
    }
}
