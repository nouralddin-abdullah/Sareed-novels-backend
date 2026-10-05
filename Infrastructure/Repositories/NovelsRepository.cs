using Domain.Constants;
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
                n.Id,
                n.Slug,
                n.LastUpdatedAt,
                AuthorUserName = n.Owner.UserName,
                Genres = n.NovelGenres.OrderBy(ng => ng.GenreId).Select(ng => ng.Genre.Slug).ToList(),
                // A chapter's page came out when the chapter did (#39): a draft published later, when it was
                // published, not written; published again, when it first came out.
                Chapters = n.Chapters
                    .Where(c => c.Status == ChapterStatuses.Published)
                    .OrderBy(c => c.ChapterIndex)
                    .Select(c => new { c.Id, c.PublishedAt })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var wiki = await GetIndexableWikiEntriesAsync(cancellationToken);

        return rows
            .Where(n => n.Chapters.Count > 0)
            .Select(n => new NovelSitemapEntry(
                n.Slug,
                LastModified(n.LastUpdatedAt, n.Chapters.Max(c => c.PublishedAt)),
                n.Chapters.Select(c => new ChapterSitemapEntry(c.Id, c.PublishedAt)).ToList(),
                n.Id,
                n.AuthorUserName,
                n.Genres,
                wiki.TryGetValue(n.Id, out var entries) ? entries : []))
            .OrderByDescending(n => n.LastModified)
            .ToList();
    }

    /// <summary>
    /// A novel page's last change: the later of its <see cref="Novel.LastUpdatedAt"/> (a chapter came out) and when its
    /// newest published chapter came out.
    /// </summary>
    private static DateTime LastModified(DateTime lastUpdatedAt, DateTime? newestChapterOut) =>
        newestChapterOut > lastUpdatedAt ? newestChapterOut.Value : lastUpdatedAt;

    /// <summary>
    /// The wiki entries of public novels that are worth indexing (<see cref="WikiPages"/>), by novel. Deleted entries,
    /// articles and novels are hidden by the query filters.
    /// </summary>
    private async Task<Dictionary<Guid, List<WikiSitemapEntry>>> GetIndexableWikiEntriesAsync(CancellationToken cancellationToken)
    {
        var entities = await dbContext.NovelEntities
            .AsNoTracking()
            .Where(e => !e.Novel.IsDraft)
            .Select(e => new
            {
                e.Id,
                e.NovelId,
                e.Name,
                e.ShortDescription,
                e.Description,
                e.Role,
                e.AttributesJson,
                e.CreatedAt,
                e.UpdatedAt,
                Articles = e.Articles.Select(a => new { a.Title, a.Content, a.UpdatedAt }).ToList()
            })
            .ToListAsync(cancellationToken);

        return entities
            .Where(e => WikiPages.IsIndexable(
                e.Name, e.ShortDescription, e.Description, e.Role, e.AttributesJson,
                e.Articles.Select(a => ((string?)a.Title, (string?)a.Content))))
            .GroupBy(e => e.NovelId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(e => e.CreatedAt)
                    .Select(e => new WikiSitemapEntry(
                        e.Id,
                        e.Articles.Select(a => a.UpdatedAt).Append(e.UpdatedAt).Max()))
                    .ToList());
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
        .Where(n => n.IsEligibleForRanking)
        .Readable()
        .Include(n => n.NovelGenres)
            .ThenInclude(ng => ng.Genre)
        // The author New Arrivals shows (#68), as the account is now, joined in the same query.
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
        // Published chapters only, as the chapter list, search and recommendations count them (drafts are the author's).
        var novel = dbContext.Novels.Where(n => n.Id == novelId);
        if (lastUpdatedAt is { } updatedAt)
        {
            // Forward only, in the same statement: of two chapters coming out at once, the later one stays (#39).
            await novel.ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ChapterCount, n => n.Chapters.Count(c => c.Status == ChapterStatuses.Published))
                .SetProperty(n => n.LastUpdatedAt, n => n.LastUpdatedAt > updatedAt ? n.LastUpdatedAt : updatedAt));
        }
        else
        {
            await novel.ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ChapterCount, n => n.Chapters.Count(c => c.Status == ChapterStatuses.Published)));
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

    public async Task<(IEnumerable<Novel>, int)> GetUserPublishedWorks(string userId, int pageNumber, int pageSize, bool readableOnly)
    {
        var query = dbContext.Novels
            .AsNoTracking()
            .Where(n => n.AuthorId == userId && !n.IsDraft && !n.IsDeleted);
        if (readableOnly)
        {
            query = query.Readable();
        }

        // Counted with the same filter as the page, so the total is of what the pages list.
        var totalCount = await query.CountAsync();

        // Ties go by id, so pages never repeat or skip a novel.
        var novels = await query
            .Include(n => n.NovelGenres)
                .ThenInclude(ng => ng.Genre)
            .Include(n => n.Owner)
            .OrderByDescending(n => n.LastUpdatedAt)
            .ThenBy(n => n.Id)
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

    public async Task<bool> SoftDeleteAsync(Guid novelId) =>
        // The query filter leaves out a novel that is already deleted.
        await dbContext.Novels
            .Where(n => n.Id == novelId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsDeleted, true)
                .SetProperty(n => n.IsEligibleForRanking, false)) > 0;

    public async Task<int> GetPublishedChaptersCountAsync(Guid novelId)
    {
        return await dbContext.Chapters
            .Where(c => c.NovelId == novelId && c.Status == "Published")
            .CountAsync();
    }

    public async Task<Dictionary<Guid, int?>> GetWordsCountsAsync(IReadOnlyCollection<Guid> novelIds)
    {
        if (novelIds.Count == 0)
        {
            return [];
        }

        var counted = await dbContext.Chapters
            .AsNoTracking()
            .Where(c => novelIds.Contains(c.NovelId))
            .GroupBy(c => c.NovelId)
            .Select(g => new { NovelId = g.Key, Words = g.Sum(c => c.WordsCount ?? 0), Uncounted = g.Count(c => c.WordsCount == null) })
            .ToDictionaryAsync(n => n.NovelId);

        return novelIds.Distinct().ToDictionary(id => id, id => counted.TryGetValue(id, out var novel)
            ? novel.Uncounted > 0 ? null : (int?)novel.Words
            : 0);
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

    public async Task<Dictionary<Guid, (string Slug, string Title)>> GetSlugsAndTitlesAsync(IReadOnlyCollection<Guid> novelIds)
    {
        if (novelIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Novels
            .AsNoTracking()
            .Where(n => novelIds.Contains(n.Id))
            .Select(n => new { n.Id, n.Slug, n.Title })
            .ToDictionaryAsync(n => n.Id, n => (n.Slug, n.Title));
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

