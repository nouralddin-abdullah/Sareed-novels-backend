using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Application.Services;
using System.Net;

namespace BackgroundJobs.Functions;

/// <summary>
/// HTTP-triggered functions for manual testing
/// Use these to test without waiting for timers
/// </summary>
public class TestTriggersFunction(
    ILogger<TestTriggersFunction> logger,
    IRankingService rankingService,
    IPrivilegeService privilegeService)
{
    [Function("TestRankingRecalculation")]
    public async Task<HttpResponseData> TestRanking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        logger.LogInformation("🧪 Manual test: Ranking recalculation triggered");
        
        try
        {
            var startTime = DateTime.UtcNow;
            await rankingService.CalculateAllGenreRankings();
            var duration = DateTime.UtcNow - startTime;
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"✅ Ranking recalculation completed in {duration.TotalMilliseconds}ms");
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Test failed");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"❌ Error: {ex.Message}");
            return response;
        }
    }
    
    [Function("TestDailyUnlock")]
    public async Task<HttpResponseData> TestDailyUnlock(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        logger.LogInformation("🧪 Manual test: Daily unlock triggered");
        
        try
        {
            var startTime = DateTime.UtcNow;
            await privilegeService.PerformDailyUnlockAsync();
            var duration = DateTime.UtcNow - startTime;
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"✅ Daily unlock completed in {duration.TotalMilliseconds}ms");
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Test failed");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"❌ Error: {ex.Message}");
            return response;
        }
    }
}
