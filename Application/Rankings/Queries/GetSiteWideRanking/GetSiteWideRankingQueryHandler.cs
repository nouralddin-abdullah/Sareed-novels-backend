using Application.Common;
using Application.Novels.DTOS;
using AutoMapper;
using Domain.Exceptions;
using Domain.Ranking;
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
    private static readonly string[] SiteWideTypes = [RankingTypes.Trending, RankingTypes.AllTime, RankingTypes.NewArrivals];

    public async Task<PagedResult<NovelInRankingDto>> Handle(GetSiteWideRankingQuery request, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var pageNumber = Math.Max(1, request.PageNumber);

        var rankingType = RankingTypes.Normalize(request.RankingType);
        if (rankingType == null || !SiteWideTypes.Contains(rankingType))
        {
            throw new NotFoundException(
                $"Invalid ranking type '{request.RankingType}'. Valid types: {string.Join(", ", SiteWideTypes)}");
        }

        logger.LogInformation("Getting {RankingType} site-wide ranking", rankingType);

        if (rankingType == RankingTypes.NewArrivals)
        {
            // Real-time: newest published novels that have at least one published chapter.
            var (novels, totalCount) = await novelsRepository.GetLatestNovels(pageSize, pageNumber);
            return new PagedResult<NovelInRankingDto>(
                mapper.Map<IEnumerable<NovelInRankingDto>>(novels), totalCount, pageSize, pageNumber);
        }

        var rankingList = await rankingRepository.GetSiteWideRankingListByType(rankingType);
        if (rankingList == null || rankingList.TotalNovels == 0)
        {
            return new PagedResult<NovelInRankingDto>([], 0, pageSize, pageNumber);
        }

        var rankingEntries = await rankingRepository.GetRankingEntriesPaged(rankingList.Id, pageSize, pageNumber);
        return new PagedResult<NovelInRankingDto>(
            mapper.Map<IEnumerable<NovelInRankingDto>>(rankingEntries), rankingList.TotalNovels, pageSize, pageNumber);
    }
}
