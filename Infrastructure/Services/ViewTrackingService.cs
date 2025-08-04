using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class ViewTrackingService(ApplicationDbContext dbContext, ILogger<ViewTrackingService> logger) : IViewTrackingService
    {
        public async Task<int> GetTodaysViews(Guid novelId)
        {
            var today = DateTime.UtcNow.Date;
            var viewRecord = await dbContext.NovelViews
                .FirstOrDefaultAsync(nv => nv.NovelId == novelId && nv.ViewDate == today);

            return viewRecord?.ViewCount ?? 0;
        }

        public async Task TrackNovelView(Guid novelId)
        {
            try
            {
                var today = DateTime.UtcNow.Date;

                // Find or create today's view record
                var viewRecord = await dbContext.NovelViews
                    .FirstOrDefaultAsync(nv => nv.NovelId == novelId && nv.ViewDate == today);

                if (viewRecord == null)
                {
                    // Create new record for today
                    viewRecord = new NovelViews
                    {
                        NovelId = novelId,
                        ViewDate = today,
                        ViewCount = 1
                    };
                    await dbContext.NovelViews.AddAsync(viewRecord);
                }
                else
                {
                    // Increment existing record
                    viewRecord.ViewCount++;
                    dbContext.NovelViews.Update(viewRecord);
                }

                // Also update novel's total views and today's views
                var novel = await dbContext.Novels.FindAsync(novelId);
                if (novel != null)
                {
                    novel.TotalViews++;
                    novel.ViewsToday++;
                    novel.LastViewUpdate = DateTime.UtcNow;
                    dbContext.Novels.Update(novel);
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to track view for novel {NovelId}", novelId);
            }
        }
    }
}
