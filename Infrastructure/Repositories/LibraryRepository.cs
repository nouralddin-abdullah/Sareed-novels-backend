using Domain.Entities;
using Domain.Library;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class LibraryRepository(ApplicationDbContext dbContext) : ILibraryRepository
{
    private const string PublishedStatus = "Published";

    public async Task<(IReadOnlyList<LibraryEntry> Entries, int TotalCount)> GetUserLibraryAsync(string userId, int pageNumber, int pageSize)
    {
        var query = VisibleProgress(userId);
        var totalCount = await query.CountAsync();

        var rows = await ToRows(query
            .OrderByDescending(p => p.LastReadAt)
            .ThenBy(p => p.NovelId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize))
            .ToListAsync();

        return (await WithPublishedChapters(rows), totalCount);
    }

    public async Task<LibraryEntry?> GetLibraryEntryAsync(string userId, Guid novelId)
    {
        var rows = await ToRows(VisibleProgress(userId).Where(p => p.NovelId == novelId)).ToListAsync();
        return (await WithPublishedChapters(rows)).SingleOrDefault();
    }

    public async Task<bool> SaveProgressAsync(string userId, Guid novelId, Guid chapterId, int chapterNumber, DateTime readAt)
    {
        if (await UpdateProgress(userId, novelId, chapterId, chapterNumber, readAt) > 0)
        {
            return false;
        }

        // First read of this novel: insert the row and count it in the user's library in one transaction. The
        // UPDLOCK/HOLDLOCK existence check makes a concurrent first read (two tabs) insert only once.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO UserNovelProgress (UserId, NovelId, LastReadChapterId, LastReadChapterNumber, LastReadAt, CreatedAt)
            SELECT {userId}, {novelId}, {chapterId}, {chapterNumber}, {readAt}, {readAt}
            WHERE NOT EXISTS (
                SELECT 1 FROM UserNovelProgress WITH (UPDLOCK, HOLDLOCK)
                WHERE UserId = {userId} AND NovelId = {novelId})
            """);

        if (inserted == 1)
        {
            await dbContext.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LibraryNovelsCount, u => u.LibraryNovelsCount + 1));
        }
        else
        {
            await UpdateProgress(userId, novelId, chapterId, chapterNumber, readAt);
        }

        await transaction.CommitAsync();
        return inserted == 1;
    }

    public async Task<List<string>> GetUsersWithNovelInLibrary(Guid novelId)
    {
        return await dbContext.UserNovelProgress
            .Where(unp => unp.NovelId == novelId)
            .Select(unp => unp.UserId)
            .Distinct()
            .ToListAsync();
    }

    private Task<int> UpdateProgress(string userId, Guid novelId, Guid chapterId, int chapterNumber, DateTime readAt) =>
        dbContext.UserNovelProgress
            .Where(p => p.UserId == userId && p.NovelId == novelId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.LastReadChapterId, chapterId)
                .SetProperty(p => p.LastReadChapterNumber, chapterNumber)
                .SetProperty(p => p.LastReadAt, readAt));

    /// <summary>Progress rows for novels readers can open (the Novel query filter already drops deleted ones).</summary>
    private IQueryable<UserNovelProgress> VisibleProgress(string userId) =>
        dbContext.UserNovelProgress.Where(p => p.UserId == userId && !p.Novel.IsDraft && !p.Novel.IsDeleted);

    private static IQueryable<ProgressRow> ToRows(IQueryable<UserNovelProgress> query) =>
        query.Select(p => new ProgressRow(
            p.NovelId,
            p.Novel.Title,
            p.Novel.Slug,
            p.Novel.CoverImageUrl,
            p.Novel.TotalAverageScore,
            p.Novel.TotalViews,
            p.Novel.Owner.UserName!,
            p.Novel.Owner.DisplayName,
            p.Novel.Owner.ProfilePhoto,
            p.LastReadChapterId,
            p.LastReadChapter.Title,
            p.LastReadChapter.ChapterIndex,
            p.LastReadAt));

    /// <summary>Loads the published chapter outlines (no content) for all rows' novels in one query.</summary>
    private async Task<IReadOnlyList<LibraryEntry>> WithPublishedChapters(List<ProgressRow> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var novelIds = rows.Select(r => r.NovelId).Distinct().ToList();
        var chapters = await dbContext.Chapters
            .Where(c => novelIds.Contains(c.NovelId) && c.Status == PublishedStatus)
            .OrderBy(c => c.ChapterIndex)
            .Select(c => new { c.NovelId, c.Id, c.Title, c.ChapterIndex })
            .ToListAsync();

        var byNovel = chapters
            .GroupBy(c => c.NovelId)
            .ToDictionary(g => g.Key, g => g.Select(c => new ChapterOutline(c.Id, c.Title, c.ChapterIndex)).ToList());

        return rows.Select(r => new LibraryEntry(
            r.NovelId,
            r.Title,
            r.Slug,
            r.CoverImageUrl,
            r.TotalAverageScore,
            r.TotalViews,
            r.AuthorUserName,
            r.AuthorDisplayName,
            r.AuthorProfilePhoto,
            new ChapterOutline(r.LastReadChapterId, r.LastReadChapterTitle, r.LastReadChapterIndex),
            r.LastReadAt,
            byNovel.TryGetValue(r.NovelId, out var published) ? published : [])).ToList();
    }

    private sealed record ProgressRow(
        Guid NovelId,
        string Title,
        string Slug,
        string CoverImageUrl,
        decimal TotalAverageScore,
        int TotalViews,
        string AuthorUserName,
        string AuthorDisplayName,
        string? AuthorProfilePhoto,
        Guid LastReadChapterId,
        string LastReadChapterTitle,
        int LastReadChapterIndex,
        DateTime LastReadAt);
}
