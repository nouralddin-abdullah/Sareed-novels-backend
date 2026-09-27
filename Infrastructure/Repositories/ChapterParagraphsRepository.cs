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

    public async Task<RemovedParagraphComments> SaveEditedParagraphs(
        Guid chapterId, IReadOnlyList<ChapterParagraph> paragraphs, IReadOnlyList<ChapterParagraph> removed)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        // A reader commenting, replying or liking on a removed paragraph at this moment can deadlock with this edit
        // (their new row waits for the paragraph, this edit waits for their row). The edit must not be the one that
        // fails. Pooled connections reset the setting when they are reused.
        await dbContext.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY HIGH");

        // Kept paragraphs are the tracked rows the caller changed in place, so only the columns the edit changed are
        // written; CommentsCount never is, since it moves with atomic SQL.
        foreach (var paragraph in paragraphs)
        {
            if (dbContext.Entry(paragraph).State == EntityState.Detached)
            {
                dbContext.ChapterParagraphs.Add(paragraph);
            }
        }

        // A negative OrderIndex marks the removed paragraphs for the set-based comment delete below. Writing it also
        // locks their rows until commit, so a comment posted on one of them from now on fails instead of landing
        // between the comment delete and the paragraph delete.
        foreach (var paragraph in removed)
        {
            if (dbContext.Entry(paragraph).State == EntityState.Detached)
            {
                dbContext.ChapterParagraphs.Attach(paragraph);
            }

            paragraph.OrderIndex = -1;
        }

        await dbContext.SaveChangesAsync();

        var deleted = removed.Count == 0
            ? RemovedParagraphComments.None
            : await SocialCounters.DeleteCommentsOnRemovedParagraphs(dbContext, chapterId);

        dbContext.ChapterParagraphs.RemoveRange(removed);
        await dbContext.SaveChangesAsync();

        await transaction.CommitAsync();
        return deleted;
    }
}
