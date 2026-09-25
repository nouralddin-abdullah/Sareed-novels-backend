using Domain.Entities;
using Domain.Repositories;
using Domain.Seo;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class NovelsRepository(ApplicationDbContext dbContext) : INovelsRepository
{
    public async Task<List<NovelSitemapEntry>> GetSitemapEntriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Novels
            .AsNoTracking()
            .Where(n => !n.IsDraft)
            .Select(n => new
            {
                n.Slug,
                n.LastUpdatedAt,
                Chapters = n.Chapters
                    .Where(c => c.Status == "Published")
                    .OrderBy(c => c.ChapterIndex)
                    .Select(c => new { c.Id, c.CreatedAt })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(n => n.Chapters.Count > 0)
            .Select(n => new NovelSitemapEntry(
                n.Slug,
                new[] { n.LastUpdatedAt, n.Chapters.Max(c => c.CreatedAt) }.Max(),
                n.Chapters.Select(c => new ChapterSitemapEntry(c.Id, c.CreatedAt)).ToList()))
            .OrderByDescending(n => n.LastModified)
            .ToList();
    }

    public async Task<bool> CreateNovel(Novel novel)
    {
        await dbContext.Novels.AddAsync(novel);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }

    public async Task<(IEnumerable<Novel>, int)> GetLatestNovels(int pageSize, int pageNumber)
    {
        // Only published novels a reader can actually open: not a draft and at least one published chapter.
        var query = dbContext.Novels
        .AsNoTracking()
        .Where(n => n.IsEligibleForRanking && !n.IsDraft && n.Chapters.Any(c => c.Status == "Published"))
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

    public async Task RefreshChapterCountAsync(Guid novelId, DateTime? lastUpdatedAt = null)
    {
        var novel = dbContext.Novels.Where(n => n.Id == novelId);
        if (lastUpdatedAt is { } updatedAt)
        {
            await novel.ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ChapterCount, n => n.Chapters.Count())
                .SetProperty(n => n.LastUpdatedAt, updatedAt));
        }
        else
        {
            await novel.ExecuteUpdateAsync(s => s.SetProperty(n => n.ChapterCount, n => n.Chapters.Count()));
        }

        // A tracked copy would otherwise write its stale count back on the next SaveChanges.
        var tracked = dbContext.Novels.Local.FirstOrDefault(n => n.Id == novelId);
        if (tracked != null)
        {
            await dbContext.Entry(tracked).ReloadAsync();
        }
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
            userWork = userWork.OrderByDescending(f => f.LastUpdatedAt).Skip(PageSize * (PageNumber - 1)).Take(PageSize);
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
            .OrderByDescending(n => n.LastUpdatedAt)
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

    public async Task<bool> SetCoverUrlAsync(Guid novelId, string coverUrl, string? expectedUrl = null, CancellationToken cancellationToken = default)
    {
        var novels = dbContext.Novels.Where(n => n.Id == novelId);
        if (expectedUrl is not null)
        {
            novels = novels.Where(n => n.CoverImageUrl == expectedUrl);
        }
        return await novels.ExecuteUpdateAsync(s => s.SetProperty(n => n.CoverImageUrl, coverUrl), cancellationToken) > 0;
    }

    public async Task<List<NovelCoverRef>> GetCoversNotMatchingAsync(string standardMarker, Guid? afterId, int take, CancellationToken cancellationToken = default)
    {
        var novels = dbContext.Novels.AsNoTracking()
            .Where(n => !n.IsDeleted && !n.CoverImageUrl.Contains(standardMarker));
        if (afterId is { } after)
        {
            novels = novels.Where(n => n.Id.CompareTo(after) > 0);
        }
        return await novels
            .OrderBy(n => n.Id)
            .Take(take)
            .Select(n => new NovelCoverRef(n.Id, n.Title, n.CoverImageUrl))
            .ToListAsync(cancellationToken);
    }

    public async Task<(int Total, int NotMatching)> CountCoversAsync(string standardMarker, CancellationToken cancellationToken = default)
    {
        var novels = dbContext.Novels.AsNoTracking().Where(n => !n.IsDeleted);
        var total = await novels.CountAsync(cancellationToken);
        var notMatching = await novels.CountAsync(n => !n.CoverImageUrl.Contains(standardMarker), cancellationToken);
        return (total, notMatching);
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

    public async Task<List<Novel>> GetNovelsByIdsAsync(List<Guid> novelIds)
    {
        if (novelIds == null || novelIds.Count == 0)
            return new List<Novel>();

        return await dbContext.Novels
            .AsNoTracking()
            .Where(n => novelIds.Contains(n.Id))
            .Include(n => n.NovelGenres)
                .ThenInclude(ng => ng.Genre)
            .ToListAsync();
    }

    public async Task<List<Novel>> GetNovelsBySharedGenresAsync(
        List<int> genreIds,
        Guid excludeNovelId,
        int limit)
    {
        if (genreIds == null || genreIds.Count == 0)
            return new List<Novel>();

        return await dbContext.NovelGenres
            .Where(ng => genreIds.Contains(ng.GenreId) &&
                         ng.NovelId != excludeNovelId &&
                         ng.Novel.IsEligibleForRanking &&
                         !ng.Novel.IsDraft)
            .GroupBy(ng => ng.NovelId)
            .Select(g => new
            {
                NovelId = g.Key,
                SharedCount = g.Count(),
                Novel = g.First().Novel
            })
            .OrderByDescending(x => x.SharedCount)
            .ThenByDescending(x => x.Novel.TotalAverageScore)
            .Take(limit)
            .Select(x => x.Novel)
            .Include(n => n.NovelGenres)
                .ThenInclude(ng => ng.Genre)
            .ToListAsync();
    }
}

