using Application.Common;
using Application.Novels.DTOS;
using AutoMapper;
using Domain.Exceptions;
using Domain.Ranking;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetPopularByGenre;

/// <summary>
/// The genre page. "new" / "trending" / "top_rated" read the precalculated ranking lists; every other sorting
/// ("popular" = most viewed, "newest", "rating", "most_reviewed") queries the novels directly. Both honour the
/// completed/ongoing filter, and an unknown genre is a 404.
/// </summary>
public class GetNovelsInGenreQueryHandler(
    ILogger<GetNovelsInGenreQueryHandler> logger, 
    INovelGenresRepository novelGenresRepository, 
    IRankingRepository rankingRepository,
    IGenresRepository genresRepository,
    IMapper mapper) : IRequestHandler<GetNovelsInGenreQuery, PagedResult<NovelInRankingDto>>
{
    public const int MaxPageSize = 100;

    public async Task<PagedResult<NovelInRankingDto>> Handle(GetNovelsInGenreQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting novels by genre {@request}", request);

        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var pageNumber = Math.Max(1, request.PageNumber);

        var genre = await genresRepository.GetBySlug(request.Slug)
            ?? throw new NotFoundException($"Genre '{request.Slug}' not found");

        var rankingType = ToRankingType(request.Sorting);
        if (rankingType != null)
        {
            var rankingList = await rankingRepository.GetRankingListByGenreAndType(genre.Id, rankingType);
            if (rankingList == null || rankingList.TotalNovels == 0)
            {
                return new PagedResult<NovelInRankingDto>([], 0, pageSize, pageNumber);
            }

            var (entries, rankedCount) = await rankingRepository.GetRankingEntriesPaged(
                rankingList.Id, pageSize, pageNumber, request.IsCompleted);
            return new PagedResult<NovelInRankingDto>(
                mapper.Map<IEnumerable<NovelInRankingDto>>(entries), rankedCount, pageSize, pageNumber);
        }

        var (novels, totalCount) = await novelGenresRepository.GetNovelsByGenre(
            genre.Id, pageSize, pageNumber, request.Sorting, request.IsCompleted);
        return new PagedResult<NovelInRankingDto>(
            mapper.Map<IEnumerable<NovelInRankingDto>>(novels), totalCount, pageSize, pageNumber);
    }

    // Only the web app's ranking names; "popular" here means most viewed, not the AllTime list.
    private static string? ToRankingType(string? sorting) => sorting switch
    {
        "new" => RankingTypes.New,
        "trending" => RankingTypes.Trending,
        "top_rated" => RankingTypes.TopRated,
        _ => null
    };
}
