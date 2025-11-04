using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ChapterParagraphsRepository(ApplicationDbContext dbContext) : IChapterParagraphsRepository
{
    public async Task<List<ChapterParagraph>> GetChapterParagraphs(Guid chapterId)
    {
        return await dbContext.ChapterParagraphs
            .Where(p => p.ChapterId == chapterId)
            .OrderBy(p => p.OrderIndex)
            .ToListAsync();
    }

    public async Task<ChapterParagraph?> GetParagraphById(Guid paragraphId)
    {
        return await dbContext.ChapterParagraphs
            .FirstOrDefaultAsync(p => p.Id == paragraphId);
    }

    public async Task<bool> CreateParagraphs(List<ChapterParagraph> paragraphs)
    {
        await dbContext.ChapterParagraphs.AddRangeAsync(paragraphs);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UpdateParagraph(ChapterParagraph paragraph)
    {
        paragraph.UpdatedAt = DateTime.UtcNow;
        dbContext.ChapterParagraphs.Update(paragraph);
        return await dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteParagraph(Guid paragraphId)
    {
        var paragraph = await GetParagraphById(paragraphId);
        if (paragraph == null) return false;
        
        dbContext.ChapterParagraphs.Remove(paragraph);
        return await dbContext.SaveChangesAsync() > 0;
    }
}
