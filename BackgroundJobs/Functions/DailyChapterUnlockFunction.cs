using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Application.Services;

namespace BackgroundJobs.Functions;

public class DailyChapterUnlockFunction(
    ILogger<DailyChapterUnlockFunction> logger,
    IPrivilegeService privilegeService)
{
    /// <summary>
    /// Unlocks 1 chapter per day for all novels with privilege system enabled
    /// CRON: "0 0 0 * * *" = Every day at 00:00 UTC (midnight)
    /// Adjust timezone in Azure Function App settings if needed
    /// </summary>
    [Function("DailyChapterUnlock")]
    public async Task Run([TimerTrigger("0 0 0 * * *")] TimerInfo timer)
    {
        logger.LogInformation("⏰ Daily chapter unlock started at: {Time}", DateTime.UtcNow);
        
        try
        {
            var startTime = DateTime.UtcNow;
            
            await privilegeService.PerformDailyUnlockAsync();
            
            var duration = DateTime.UtcNow - startTime;
            
            logger.LogInformation(
                "✅ Daily chapter unlock completed successfully in {Duration}ms", 
                duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Daily chapter unlock failed: {Message}", ex.Message);
            throw;
        }
        
        if (timer.ScheduleStatus != null)
        {
            logger.LogInformation("Next daily unlock scheduled for: {NextRun}", timer.ScheduleStatus.Next);
        }
    }
}
