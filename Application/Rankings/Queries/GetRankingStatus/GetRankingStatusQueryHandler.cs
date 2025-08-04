using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Rankings.Queries.GetRankingStatus;

public class GetRankingStatusQueryHandler(IRankingRepository rankingRepository, ILogger<GetRankingStatusQueryHandler> logger) : IRequestHandler<GetRankingStatusQuery, GetRankingStatusResult>
{
    public async Task<GetRankingStatusResult> Handle(GetRankingStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var rankingLists = await rankingRepository.GetAllRankingLists();

            var rankingListStatuses = rankingLists.Select(rl => new RankingListStatus
            {
                Id = rl.Id,
                Name = rl.Name,
                GenreId = rl.GenreId,
                RankingType = rl.RankingType,
                LastUpdated = rl.LastUpdated,
                TotalNovels = rl.TotalNovels
            }).ToList();

            return new GetRankingStatusResult
            {
                Success = true,
                Message = "Ranking status retrieved successfully",
                Timestamp = DateTime.UtcNow,
                TotalRankingLists = rankingListStatuses.Count,
                RankingLists = rankingListStatuses
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get ranking status");
            return new GetRankingStatusResult
            {
                Success = false,
                Message = "Failed to get ranking status",
                Timestamp = DateTime.UtcNow,
                TotalRankingLists = 0,
                RankingLists = new List<RankingListStatus>()
            };
        }
    }
}
