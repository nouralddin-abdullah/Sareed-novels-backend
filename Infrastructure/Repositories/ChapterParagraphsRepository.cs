using System.Data;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    public async Task<IChapterTextEdit> BeginEditAsync(Guid chapterId)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            // A reader commenting, replying or liking on a removed paragraph at this moment can deadlock with this edit
            // (their new row waits for the paragraph, this edit waits for their row). The edit must not be the one that
            // fails. Pooled connections reset the setting when they are reused.
            await dbContext.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY HIGH");
            await HoldChapterText(chapterId);

            // Read inside the lock, as they are now: drop copies this context may hold from before.
            foreach (var entry in dbContext.ChangeTracker.Entries<ChapterParagraph>()
                         .Where(e => e.Entity.ChapterId == chapterId).ToList())
            {
                entry.State = EntityState.Detached;
            }

            var paragraphs = await dbContext.ChapterParagraphs
                .Where(p => p.ChapterId == chapterId)
                .OrderBy(p => p.OrderIndex)
                .ToListAsync();
            return new ChapterTextEdit(dbContext, transaction, chapterId, paragraphs);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The chapter's text lock, held until the transaction ends: edits of one chapter's text (the author's saves, the
    /// format maintenance) take it first and so run one after another, each reading the paragraphs the one before
    /// left. Nothing else takes it, so readers commenting meanwhile don't wait for it.
    /// </summary>
    private async Task HoldChapterText(Guid chapterId)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await dbContext.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout",
            result,
            new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = $"chapter-text:{chapterId:N}" },
            new SqlParameter("@timeout", SqlDbType.Int) { Value = (int)TextLockTimeout.TotalMilliseconds });

        // 0 or 1: granted (at once or after waiting); below 0: timed out, cancelled, or chosen as a deadlock victim.
        if (result.Value is not int status || status < 0)
        {
            throw new InvalidOperationException(
                $"Could not lock the text of chapter {chapterId} for an edit (sp_getapplock returned {result.Value}).");
        }
    }

    /// <summary>How long an edit waits for another edit of the same chapter to end (within the command timeout).</summary>
    private static readonly TimeSpan TextLockTimeout = TimeSpan.FromSeconds(20);

    private sealed class ChapterTextEdit(
        ApplicationDbContext db, IDbContextTransaction transaction, Guid chapterId, List<ChapterParagraph> paragraphs)
        : IChapterTextEdit
    {
        private bool saved;

        public IReadOnlyList<ChapterParagraph> Paragraphs => paragraphs;

        public async Task<RemovedParagraphComments> SaveAsync(
            IReadOnlyList<ChapterParagraph> edited, IReadOnlyList<ChapterParagraph> removed)
        {
            // Kept paragraphs are the tracked rows the caller changed in place, so only the columns the edit changed are
            // written; CommentsCount never is, since it moves with atomic SQL.
            foreach (var paragraph in edited)
            {
                if (db.Entry(paragraph).State == EntityState.Detached)
                {
                    db.ChapterParagraphs.Add(paragraph);
                }
            }

            // A negative OrderIndex marks the removed paragraphs for the set-based comment delete below. Writing it also
            // locks their rows until commit, so a comment posted on one of them from now on fails instead of landing
            // between the comment delete and the paragraph delete.
            foreach (var paragraph in removed)
            {
                if (db.Entry(paragraph).State == EntityState.Detached)
                {
                    db.ChapterParagraphs.Attach(paragraph);
                }

                paragraph.OrderIndex = -1;
            }

            await db.SaveChangesAsync();

            var deleted = removed.Count == 0
                ? RemovedParagraphComments.None
                : await SocialCounters.DeleteCommentsOnRemovedParagraphs(db, chapterId);

            db.ChapterParagraphs.RemoveRange(removed);
            await db.SaveChangesAsync();

            // Last, so the chapter row is held only from here to the commit, while readers may be commenting.
            await db.Chapters
                .Where(c => c.Id == chapterId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ParagraphsCount, edited.Count));

            await transaction.CommitAsync();
            saved = true;
            return deleted;
        }

        public async ValueTask DisposeAsync()
        {
            if (!saved)
            {
                // Rolled back: what the caller changed in place, or a failed save began, is undone in this context too,
                // so no later save of it writes any of it.
                foreach (var entry in db.ChangeTracker.Entries<ChapterParagraph>()
                             .Where(e => e.Entity.ChapterId == chapterId).ToList())
                {
                    if (entry.State == EntityState.Added)
                    {
                        entry.State = EntityState.Detached;
                    }
                    else if (entry.State is EntityState.Modified or EntityState.Deleted)
                    {
                        entry.CurrentValues.SetValues(entry.OriginalValues);
                        entry.State = EntityState.Unchanged;
                    }
                }
            }

            await transaction.DisposeAsync();
        }
    }
}
