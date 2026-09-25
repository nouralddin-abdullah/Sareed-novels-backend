using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class ViewTrackingService(ApplicationDbContext dbContext, ILogger<ViewTrackingService> logger) : IViewTrackingService
{
    public async Task TrackNovelView(Guid novelId, string visitorKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            if (!await TryRecordUniqueView(novelId, today, visitorKey, novelId, ViewKind.NovelPage, cancellationToken))
            {
                return;
            }

            if (await IncrementDailyViews(novelId, today, cancellationToken) == 0)
            {
                try
                {
                    dbContext.NovelViews.Add(new NovelViews { NovelId = novelId, ViewDate = today, ViewCount = 1 });
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // Another request created today's row between our UPDATE and INSERT (unique NovelId+ViewDate).
                    dbContext.ChangeTracker.Clear();
                    await IncrementDailyViews(novelId, today, cancellationToken);
                }
            }

            await dbContext.Novels
                .Where(n => n.Id == novelId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.TotalViews, n => n.TotalViews + 1)
                    .SetProperty(n => n.LastViewUpdate, DateTime.UtcNow), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to track view for novel {NovelId}", novelId);
        }
    }

    public async Task TrackChapterView(Guid chapterId, Guid novelId, string visitorKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            if (!await TryRecordUniqueView(chapterId, today, visitorKey, novelId, ViewKind.Chapter, cancellationToken))
            {
                return;
            }

            await dbContext.Chapters
                .Where(c => c.Id == chapterId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ViewsCount, c => c.ViewsCount + 1), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to track view for chapter {ChapterId}", chapterId);
        }
    }

    public Task<int> PruneUniqueViewsAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = olderThan.Date;
        return dbContext.DailyUniqueViews
            .Where(v => v.Day < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Inserts the visitor-day row unless it exists. True only for the first view of the day.</summary>
    private async Task<bool> TryRecordUniqueView(
        Guid targetId, DateTime day, string visitorKey, Guid novelId, ViewKind kind, CancellationToken cancellationToken)
    {
        var kindValue = (byte)kind;
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO DailyUniqueViews (TargetId, Day, VisitorKey, NovelId, Kind)
            SELECT {targetId}, CAST({day} AS date), {visitorKey}, {novelId}, {kindValue}
            WHERE NOT EXISTS (
                SELECT 1 FROM DailyUniqueViews WITH (UPDLOCK, HOLDLOCK)
                WHERE TargetId = {targetId} AND Day = CAST({day} AS date) AND VisitorKey = {visitorKey})
            """, cancellationToken);
        return inserted == 1;
    }

    private Task<int> IncrementDailyViews(Guid novelId, DateTime day, CancellationToken cancellationToken) =>
        dbContext.NovelViews
            .Where(v => v.NovelId == novelId && v.ViewDate == day)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ViewCount, v => v.ViewCount + 1), cancellationToken);
}
