using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ChaptersRepository(ApplicationDbContext dbContext) : IChaptersRepository
{
    public async Task<Dictionary<Guid, string>> GetTitlesAsync(IReadOnlyCollection<Guid> chapterIds) =>
        chapterIds.Count == 0
            ? []
            : await dbContext.Chapters
                .AsNoTracking()
                .Where(c => chapterIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Title })
                .ToDictionaryAsync(c => c.Id, c => c.Title);

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

    public async Task<List<Guid>> GetChapterIdsAsync(Guid? after, int take)
    {
        var chapters = dbContext.Chapters.AsNoTracking();
        if (after is { } last)
        {
            chapters = chapters.Where(c => c.Id.CompareTo(last) > 0);
        }

        return await chapters.OrderBy(c => c.Id).Select(c => c.Id).Take(take).ToListAsync();
    }

    public async Task<LegacyChapterContent> CountLegacyContentAsync() =>
        new(await dbContext.Chapters.CountAsync(c => c.Content != null),
            await dbContext.Chapters.CountAsync(c => c.Content != null && !dbContext.ChapterParagraphs.Any(p => p.ChapterId == c.Id)));

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

    public async Task<bool> ReloadAsync(Chapter chapter)
    {
        var entry = dbContext.Entry(chapter);
        await entry.ReloadAsync();
        // A chapter deleted since it was loaded is detached by the reload.
        return entry.State != EntityState.Detached;
    }

    public async Task<ChapterSave> UpdateChapter(Chapter chapter)
    {
        var entry = dbContext.Chapters.Update(chapter);
        // Comment and view counters move with atomic SQL (comments, a chapter edit's removed paragraphs, view
        // tracking). Writing back the values loaded with the chapter would undo every change since, e.g. the
        // comments an edit just deleted.
        entry.Property(c => c.CommentsCount).IsModified = false;
        entry.Property(c => c.TotalCommentsCount).IsModified = false;
        entry.Property(c => c.ViewsCount).IsModified = false;
        // The paragraph count is written with the paragraphs, inside the edit of the chapter's text
        // (ChapterParagraphsRepository.BeginEditAsync); the copy loaded here may be older than that edit.
        entry.Property(c => c.ParagraphsCount).IsModified = false;
        // When the chapter came out is stored below, only while it has none (#39): the copy loaded for this save may
        // be older than another save that published it, whose date must stay, and only one save can be its first.
        // (Not modified puts the loaded value back, so the date SetStatus gave it is taken first.)
        var publishedAt = entry.Property(c => c.PublishedAt);
        var cameOutAt = chapter.Status == ChapterStatuses.Published ? publishedAt.CurrentValue : null;
        publishedAt.IsModified = false;
        // The published sequence isn't written either (#77): only the recalculation after a publish or unpublish writes
        // it (RecalculatePublishedSequencesAsync), and a chapter of the same novel published meanwhile, by hand or on
        // schedule, renumbers this one too without holding it. The word count is written only when this save changed
        // it: the startup backfill counts older chapters' words without holding them.
        entry.Property(c => c.PublishedChapterSequence).IsModified = false;
        var wordsCount = entry.Property(c => c.WordsCount);
        wordsCount.IsModified = wordsCount.CurrentValue != wordsCount.OriginalValue;
        // The revision moves below, only from the revision this copy was loaded at (#75). (Not modified puts the loaded
        // revision back, so the one the edit gave it is taken first.)
        var revision = entry.Property(c => c.Revision);
        var (loadedRevision, newRevision) = (revision.OriginalValue, revision.CurrentValue);
        revision.IsModified = false;

        // Part of an edit of the chapter's text, it commits with the edit; otherwise in a transaction of its own.
        await using var ownTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync()
            : null;
        if (newRevision != loadedRevision
            && await dbContext.Chapters
                .Where(c => c.Id == chapter.Id && c.Revision == loadedRevision)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, newRevision)) == 0)
        {
            var stored = await dbContext.Chapters.Where(c => c.Id == chapter.Id).Select(c => c.Revision).SingleAsync();
            throw new ChapterChangedException(stored);
        }

        var saved = await dbContext.SaveChangesAsync() > 0;
        var cameOut = saved
            && cameOutAt is { } at
            && await dbContext.Chapters
                .Where(c => c.Id == chapter.Id && c.PublishedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PublishedAt, at)) > 0;
        if (ownTransaction != null)
        {
            await ownTransaction.CommitAsync();
        }

        if (cameOut)
        {
            // Stored: the tracked chapter has it too, as saved.
            publishedAt.OriginalValue = cameOutAt;
            publishedAt.CurrentValue = cameOutAt;
        }

        // Stored (or unchanged): the tracked chapter has it too.
        revision.OriginalValue = newRevision;
        revision.CurrentValue = newRevision;
        return new ChapterSave(saved, cameOut);
    }

    public async Task<List<Guid>> GetDueChapterIdsAsync(DateTime now, int max, Guid? novelId = null, string? novelSlug = null)
    {
        var due = dbContext.Chapters
            .AsNoTracking()
            .Where(c => c.Status == ChapterStatuses.Draft && c.PublishAt != null && c.PublishAt <= now && !c.Novel.IsDeleted);
        if (novelId is { } id)
        {
            due = due.Where(c => c.NovelId == id);
        }
        if (novelSlug is not null)
        {
            due = due.Where(c => c.Novel.Slug == novelSlug);
        }

        // In the order they were due, a novel's chapters in reading order: readers are told in that order too.
        return await due
            .OrderBy(c => c.PublishAt)
            .ThenBy(c => c.ChapterIndex)
            .Take(max)
            .Select(c => c.Id)
            .ToListAsync();
    }
}
