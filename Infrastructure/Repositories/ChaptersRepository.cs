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
        dbContext.Chapters.Remove(chapter);
        var result = await dbContext.SaveChangesAsync();
        return result > 0;
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
