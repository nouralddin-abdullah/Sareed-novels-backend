using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Application.Services;

namespace BackgroundJobs.Functions;

public class RankingRecalculationFunction(
    ILogger<RankingRecalculationFunction> logger,
    IRankingService rankingService)
{
    /// <summary>
    /// Recalculates all novel rankings every 6 hours
    /// CRON: "0 0 */6 * * *" = Every 6 hours at minute 0
    /// Times: 00:00, 06:00, 12:00, 18:00 UTC
    /// </summary>
    [Function("RankingRecalculation")]
    public async Task Run([TimerTrigger("0 0 */6 * * *")] TimerInfo timer)
    {
        logger.LogInformation("⏰ Ranking recalculation started at: {Time}", DateTime.UtcNow);
        
        try
        {
            var startTime = DateTime.UtcNow;
            
            await rankingService.CalculateAllGenreRankings();
            
            var duration = DateTime.UtcNow - startTime;
            
            logger.LogInformation(
                "✅ Ranking recalculation completed successfully in {Duration}ms", 
                duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Ranking recalculation failed: {Message}", ex.Message);
            throw;
        }
        
        if (timer.ScheduleStatus != null)
        {
            logger.LogInformation("Next ranking recalculation scheduled for: {NextRun}", timer.ScheduleStatus.Next);
        }
    }
}
