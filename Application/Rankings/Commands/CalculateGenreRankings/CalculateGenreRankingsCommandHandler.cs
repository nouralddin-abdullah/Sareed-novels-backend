using Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Rankings.Commands.CalculateGenreRankings;

public class CalculateGenreRankingsCommandHandler(IRankingService rankingService, ILogger<CalculateGenreRankingsCommandHandler> logger) : IRequestHandler<CalculateGenreRankingsCommand, CalculateGenreRankingsResult>
{
    public async Task<CalculateGenreRankingsResult> Handle(CalculateGenreRankingsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting calculation of rankings for genre {GenreId}", request.GenreId);
            var startTime = DateTime.UtcNow;

            await rankingService.CalculateGenreRankings(request.GenreId, request.RankingType);

            var duration = DateTime.UtcNow - startTime;
            logger.LogInformation("Completed calculation of rankings for genre {GenreId} in {Duration}ms",
                request.GenreId, duration.TotalMilliseconds);

            return new CalculateGenreRankingsResult
            {
                Success = true,
                Message = $"أُعيد حساب ترتيب التصنيف رقم {request.GenreId}",
                GenreId = request.GenreId,
                RankingType = request.RankingType,
                ExecutionTimeMs = duration.TotalMilliseconds,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to calculate rankings for genre {GenreId}", request.GenreId);
            return new CalculateGenreRankingsResult
            {
                Success = false,
                Code = "OperationFailed",
                Message = $"تعذّر حساب ترتيب التصنيف رقم {request.GenreId}",
                GenreId = request.GenreId,
                RankingType = request.RankingType,
                ExecutionTimeMs = 0,
                Timestamp = DateTime.UtcNow,
                Error = ex.Message
            };
        }
        throw new NotImplementedException();
    }
}
