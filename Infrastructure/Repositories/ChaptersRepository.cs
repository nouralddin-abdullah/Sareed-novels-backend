using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ChaptersRepository(ApplicationDbContext dbContext) : IChaptersRepository
{
    public async Task<bool> CreateChapter(Chapter chapter)
    {
        await dbContext.AddAsync(chapter);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }

    public async Task<bool> DeleteChapter(Chapter chapter)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await MoveReadersOffChapter(chapter);
        // Comment likes, replies and paragraph comments reference the chapter's comments without a cascade.
        await SocialCounters.DeleteChapterComments(dbContext, chapter.Id);
        dbContext.Chapters.Remove(chapter);
        var result = await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return result > 0;
    }

    /// <summary>
    /// Reading progress points at its chapter with a no-cascade foreign key, so a chapter someone stopped at can't be
    /// deleted as is. Those readers move to the nearest other chapter, preferring a published one before it; if the
    /// novel has no other chapter, their progress row (and its count in their library) goes.
    /// </summary>
    private async Task MoveReadersOffChapter(Chapter chapter)
    {
        var readers = dbContext.UserNovelProgress.Where(p => p.LastReadChapterId == chapter.Id);
        if (!await readers.AnyAsync())
        {
            return;
        }

        var replacement = await dbContext.Chapters
            .Where(c => c.NovelId == chapter.NovelId && c.Id != chapter.Id)
            .OrderByDescending(c => c.Status == "Published")
            .ThenBy(c => c.ChapterIndex <= chapter.ChapterIndex ? 0 : 1)
            .ThenBy(c => c.ChapterIndex <= chapter.ChapterIndex ? chapter.ChapterIndex - c.ChapterIndex : c.ChapterIndex - chapter.ChapterIndex)
            .Select(c => new { c.Id, c.ChapterIndex })
            .FirstOrDefaultAsync();

        if (replacement == null)
        {
            var userIds = await readers.Select(p => p.UserId).ToListAsync();
            await readers.ExecuteDeleteAsync();
            await dbContext.Users
                .Where(u => userIds.Contains(u.Id) && u.LibraryNovelsCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LibraryNovelsCount, u => u.LibraryNovelsCount - 1));
            return;
        }

        // The replacement's position among published chapters once this chapter is gone.
        var chapterNumber = await dbContext.Chapters.CountAsync(c =>
            c.NovelId == chapter.NovelId && c.Id != chapter.Id && c.Status == "Published" && c.ChapterIndex <= replacement.ChapterIndex);

        await readers.ExecuteUpdateAsync(s => s
            .SetProperty(p => p.LastReadChapterId, replacement.Id)
            .SetProperty(p => p.LastReadChapterNumber, chapterNumber));
    }

    public async Task<Chapter?> GetChapterById(Guid chapterId)
    {
        return await dbContext.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId);
    }

    public async Task<Chapter?> GetChapterBySlug(string slug)
    {
        return await dbContext.Chapters
            .Include(c => c.Novel)
            .ThenInclude(n => n.Owner)
            .FirstOrDefaultAsync(c => c.Slug == slug);  
    }

    public async Task<IEnumerable<Chapter>> GetChaptersAuthorView(Guid novelId)
    {
        return await dbContext.Chapters.Where(c => c.NovelId == novelId).OrderBy(c=> c.ChapterIndex).ToListAsync();
    }

    public async Task<IEnumerable<Chapter>> GetChaptersReaderView(Guid novelId)
    {
        return await dbContext.Chapters.Where(c => c.NovelId == novelId && c.Status == "Published").OrderBy(c => c.ChapterIndex).ToListAsync();
    }

    public async Task<int> GetNextChapterIndex(Guid novelId)
    {
        var maxIndex = await dbContext.Chapters.Where(c => c.NovelId == novelId).MaxAsync(c => (int?)c.ChapterIndex) ?? 0;
        return maxIndex + 1;
    }

    public async Task<string?> GetNextChapterSlug(Guid novelId, int currentChapterIndex)
    {
        var nextChapter = await dbContext.Chapters.Where(c => c.NovelId == novelId && c.Status == "Published" && c.ChapterIndex > currentChapterIndex)
            .OrderBy(c => c.ChapterIndex)
            .Select(c => c.Slug)
            .FirstOrDefaultAsync();

        return nextChapter;
    }

    public async Task<bool> ReorderChapters(Guid novelId, List<Guid> orderedChapterIds)
    {
        var chapters = await dbContext.Chapters
        .Where(c => c.NovelId == novelId)
        .ToListAsync();

        var existingChapterIds = chapters.Select(c => c.Id).ToHashSet();
        var providedChapterIds = orderedChapterIds.ToHashSet();

        if (!existingChapterIds.SetEquals(providedChapterIds))
        {
            return false;
        }
        var updates = new List<object>();
        for (int i = 0; i < orderedChapterIds.Count; i++)
        {
            var chapterId = orderedChapterIds[i];
            var chapter = chapters.First(c => c.Id == chapterId);
            chapter.ChapterIndex = i + 1;
        }
        using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            return false;
        }
    }

    public async Task<bool> UpdateChapter(Chapter chapter)
    {
        dbContext.Chapters.Update(chapter);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
    }
}
