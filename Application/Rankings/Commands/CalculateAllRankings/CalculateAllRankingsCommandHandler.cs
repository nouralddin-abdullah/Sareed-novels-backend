using Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Rankings.Commands.CalculateAllRankings;

internal class CalculateAllRankingsCommandHandler(IRankingService rankingService, ILogger<CalculateAllRankingsCommandHandler> logger) : IRequestHandler<CalculateAllRankingsCommand, CalculateAllRankingsResult>
{
    public async Task<CalculateAllRankingsResult> Handle(CalculateAllRankingsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting calculation of all genre rankings");
            var startTime = DateTime.UtcNow;

            await rankingService.CalculateAllGenreRankings();

            var duration = DateTime.UtcNow - startTime;
            logger.LogInformation("Completed calculation of all genre rankings in {Duration}ms", duration.TotalMilliseconds);

            return new CalculateAllRankingsResult
            {
                Success = true,
                Message = "أُعيد حساب ترتيب كل التصنيفات",
                ExecutionTimeMs = duration.TotalMilliseconds,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to calculate all genre rankings");
            return new CalculateAllRankingsResult
            {
                Success = false,
                Code = "OperationFailed",
                Message = "تعذّر حساب الترتيب",
                ExecutionTimeMs = 0,
                Timestamp = DateTime.UtcNow,
                Error = ex.Message
            };
        }
    }
}
