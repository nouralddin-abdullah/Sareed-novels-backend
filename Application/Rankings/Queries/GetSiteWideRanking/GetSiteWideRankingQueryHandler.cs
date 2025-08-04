using Application.Common;
using Application.Novels.DTOS;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Rankings.Queries.GetSiteWideRanking;

public class GetSiteWideRankingQueryHandler(
    IRankingRepository rankingRepository, 
    INovelsRepository novelsRepository,
    ILogger<GetSiteWideRankingQueryHandler> logger, 
    IMapper mapper) : IRequestHandler<GetSiteWideRankingQuery, PagedResult<NovelInRankingDto>>
{
    public async Task<PagedResult<NovelInRankingDto>> Handle(GetSiteWideRankingQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting {RankingType} site-wide ranking", request.RankingType);

        // Handle NewArrivals as real-time query
        if (request.RankingType.Equals("NewArrivals", StringComparison.OrdinalIgnoreCase))
        {
            return await GetNewArrivalsRealTime(request);
        }

        // Handle precalculated rankings (AllTime, Trending)
        return await GetPrecalculatedRanking(request);
    }

    private async Task<PagedResult<NovelInRankingDto>> GetNewArrivalsRealTime(GetSiteWideRankingQuery request)
    {
        logger.LogInformation("Getting real-time new arrivals");

        // Get novels directly from database, ordered by creation date
        var (novels, totalCount) = await novelsRepository.GetLatestNovels(
            request.PageSize, 
            request.PageNumber);

        // Map to DTOs
        var novelDtos = mapper.Map<IEnumerable<NovelInRankingDto>>(novels);

        return new PagedResult<NovelInRankingDto>(
            novelDtos,
            totalCount,
            request.PageSize,
            request.PageNumber);
    }

    private async Task<PagedResult<NovelInRankingDto>> GetPrecalculatedRanking(GetSiteWideRankingQuery request)
    {
        // Validate ranking type
        var validTypes = new[] { "AllTime", "Trending" };
        if (!validTypes.Contains(request.RankingType))
        {
            throw new NotFoundException($"Invalid ranking type '{request.RankingType}'. Valid types: {string.Join(", ", validTypes)} or NewArrivals");
        }

        // Get site-wide ranking list (GenreId = null)
        var rankingList = await rankingRepository.GetSiteWideRankingListByType(request.RankingType)
            ?? throw new NotFoundException($"No {request.RankingType} site-wide ranking found. Rankings may not be calculated yet.");

        // Get ranking entries with pagination
        var rankingEntries = await rankingRepository.GetRankingEntriesPaged(
            rankingList.Id,
            request.PageSize,
            request.PageNumber);

        // Map to NovelInRankingDto
        var novelDtos = mapper.Map<IEnumerable<NovelInRankingDto>>(rankingEntries);

        return new PagedResult<NovelInRankingDto>(
            novelDtos,
            rankingList.TotalNovels,
            request.PageSize,
            request.PageNumber);
    }
}
