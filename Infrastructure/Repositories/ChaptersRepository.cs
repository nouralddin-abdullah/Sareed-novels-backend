using Domain.Constants;
using Domain.Entities;
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

    public async Task<ChapterSave> UpdateChapter(Chapter chapter, bool withStatus = true)
    {
        var entry = dbContext.Chapters.Update(chapter);
        // Comment and view counters move with atomic SQL (comments, a chapter edit's removed paragraphs, view
        // tracking). Writing back the values loaded with the chapter would undo every change since, e.g. the
        // comments an edit just deleted.
        entry.Property(c => c.CommentsCount).IsModified = false;
        entry.Property(c => c.TotalCommentsCount).IsModified = false;
        entry.Property(c => c.ViewsCount).IsModified = false;
        // When the chapter came out is stored below, only while it has none (#39): the copy loaded for this save may
        // be older than another save that published it, whose date must stay, and only one save can be its first.
        // (Not modified puts the loaded value back, so the date SetStatus gave it is taken first.)
        var publishedAt = entry.Property(c => c.PublishedAt);
        var cameOutAt = chapter.Status == ChapterStatuses.Published ? publishedAt.CurrentValue : null;
        publishedAt.IsModified = false;
        // Nor the status, the schedule, the published sequence or the word count (#77): the schedule may have published
        // the chapter, cleared its schedule and numbered it since this copy was loaded, and the startup backfill may have
        // counted its words. The status is stored below only by a save that sets one, and only where the chapter has
        // another, so one save (or the schedule) changes it; the schedule only when this save changes it, and only on a
        // draft; the sequence only by the recalculation after a publish (RecalculatePublishedSequencesAsync); the word
        // count only when it changed.
        entry.Property(c => c.PublishedChapterSequence).IsModified = false;
        var status = entry.Property(c => c.Status);
        var newStatus = status.CurrentValue;
        status.IsModified = false;
        var publishAt = entry.Property(c => c.PublishAt);
        var newPublishAt = publishAt.CurrentValue;
        var reschedules = newPublishAt != publishAt.OriginalValue;
        publishAt.IsModified = false;
        var wordsCount = entry.Property(c => c.WordsCount);
        wordsCount.IsModified = wordsCount.CurrentValue != wordsCount.OriginalValue;

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var saved = await dbContext.SaveChangesAsync() > 0;
        var statusChanged = saved && withStatus && await StoreStatusAsync(dbContext.Chapters.Where(c => c.Id == chapter.Id), newStatus);
        if (saved && reschedules)
        {
            await dbContext.Chapters
                .Where(c => c.Id == chapter.Id && c.Status == ChapterStatuses.Draft)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PublishAt, newPublishAt));
        }
        var cameOut = saved && cameOutAt is { } at && await StampCameOutAsync(chapter.Id, at);
        await transaction.CommitAsync();

        // The tracked chapter is the stored one from here: when it came out, its status, schedule and sequence, which
        // the sequences recalculated after a publish compare with (a value left as loaded would never be written).
        await entry.ReloadAsync();
        return new ChapterSave(saved, cameOut, statusChanged);
    }

    public async Task<List<DueChapter>> GetDueChaptersAsync(DateTime now, int max, Guid? novelId = null, string? novelSlug = null)
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
            .Select(c => new DueChapter(c.Id, c.NovelId, c.Slug, c.Title))
            .ToListAsync();
    }

    public async Task<ChapterSave> PublishDueAsync(Guid chapterId, DateTime now)
    {
        // The author's publish (UpdateChapter), only while the chapter is still a draft whose time has come: a run that
        // lost to another, to the author's own publish, or to a new or cancelled schedule, changes nothing.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var published = await StoreStatusAsync(
            dbContext.Chapters.Where(c => c.Id == chapterId && c.Status == ChapterStatuses.Draft && c.PublishAt != null && c.PublishAt <= now),
            ChapterStatuses.Published);
        var cameOut = published && await StampCameOutAsync(chapterId, now);
        await transaction.CommitAsync();
        return new ChapterSave(published, cameOut, published);
    }

    /// <summary>
    /// Stores <paramref name="status"/> on <paramref name="chapter"/> unless it has it already, so of two writers at
    /// once one changes it; publishing also clears the schedule (#77). True when it changed.
    /// </summary>
    private static async Task<bool> StoreStatusAsync(IQueryable<Chapter> chapter, string status)
    {
        var other = chapter.Where(c => c.Status != status);
        return status == ChapterStatuses.Published
            ? await other.ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.PublishAt, (DateTime?)null)) > 0
            : await other.ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, status)) > 0;
    }

    /// <summary>
    /// Stores when the chapter came out, <paramref name="at"/>, only while it has none (#39): true when this is its first
    /// publish.
    /// </summary>
    private async Task<bool> StampCameOutAsync(Guid chapterId, DateTime at) =>
        await dbContext.Chapters
            .Where(c => c.Id == chapterId && c.PublishedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.PublishedAt, at)) > 0;
}
