using Application.Common;
using Application.Novels.DTOS;
using AutoMapper;
using Domain.Exceptions;
using Domain.Ranking;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Rankings.Queries.GetGenreRanking;

public class GetGenreRankingQueryHandler(IRankingRepository rankingRepository, ILogger<GetGenreRankingQueryHandler> logger, IGenresRepository genresRepository, IMapper mapper) : IRequestHandler<GetGenreRankingQuery, PagedResult<NovelInRankingDto>>
{
    private static readonly string[] GenreRankingTypes = [RankingTypes.Trending, RankingTypes.TopRated, RankingTypes.New];

    public async Task<PagedResult<NovelInRankingDto>> Handle(GetGenreRankingQuery request, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var pageNumber = Math.Max(1, request.PageNumber);

        // The web app sends "top_rated" / "trending" / "new"; stored names are "TopRated" / "Trending" / "New".
        var rankingType = RankingTypes.Normalize(request.RankingType);
        if (rankingType == null || !GenreRankingTypes.Contains(rankingType))
        {
            throw new NotFoundException(
                $"Invalid ranking type '{request.RankingType}'. Valid types: {string.Join(", ", GenreRankingTypes)}");
        }

        logger.LogInformation("Getting {RankingType} ranking for genre {GenreSlug}", rankingType, request.GenreSlug);

        var genre = await genresRepository.GetBySlug(request.GenreSlug)
            ?? throw new NotFoundException($"Genre '{request.GenreSlug}' not found");

        // A genre with no qualifying novels simply has an empty list.
        var rankingList = await rankingRepository.GetRankingListByGenreAndType(genre.Id, rankingType);
        if (rankingList == null || rankingList.TotalNovels == 0)
        {
            return new PagedResult<NovelInRankingDto>([], 0, pageSize, pageNumber);
        }

        var rankingEntries = await rankingRepository.GetRankingEntriesPaged(rankingList.Id, pageSize, pageNumber);
        var novelDtos = mapper.Map<IEnumerable<NovelInRankingDto>>(rankingEntries);

        return new PagedResult<NovelInRankingDto>(novelDtos, rankingList.TotalNovels, pageSize, pageNumber);
    }
}
