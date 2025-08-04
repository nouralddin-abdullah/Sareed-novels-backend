using Application.Common;
using Application.Novels.DTOS;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetPopularByGenre;

internal class GetNovelsInGenreQueryHandler(
    ILogger<GetNovelsInGenreQueryHandler> logger, 
    INovelGenresRepository novelGenresRepository, 
    IRankingRepository rankingRepository,
    IGenresRepository genresRepository,
    IMapper mapper) : IRequestHandler<GetNovelsInGenreQuery, PagedResult<NovelInRankingDto>>
{
    public async Task<PagedResult<NovelInRankingDto>> Handle(GetNovelsInGenreQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting novels by genre {@request}", request);

        // Check if this is a precalculated ranking request
        if (IsPrecalculatedRanking(request.Sorting))
        {
            return await GetPrecalculatedRanking(request);
        }

        // Handle simple sorting (immediate database queries)
        var (result, totalCount) = await novelGenresRepository.GetNovelsByGenre(
            request.Slug, request.PageSize, request.PageNumber, request.Sorting, request.IsCompleted);
        
        var resultDto = mapper.Map<IEnumerable<NovelInRankingDto>>(result);
        return new PagedResult<NovelInRankingDto>(resultDto, totalCount, request.PageSize, request.PageNumber);
    }

    private static bool IsPrecalculatedRanking(string sorting)
    {
        return sorting is "new" or "trending" or "top_rated";
    }

    private async Task<PagedResult<NovelInRankingDto>> GetPrecalculatedRanking(GetNovelsInGenreQuery request)
    {
        // Convert sorting to ranking type
        var rankingType = request.Sorting switch
        {
            "new" => "New",
            "trending" => "Trending", 
            "top_rated" => "TopRated",
            _ => "TopRated"
        };

        // Get genre by slug
        var genre = await genresRepository.GetBySlug(request.Slug);
        if (genre == null)
        {
            return new PagedResult<NovelInRankingDto>([], 0, request.PageSize, request.PageNumber);
        }

        // Get ranking list
        var rankingList = await rankingRepository.GetRankingListByGenreAndType(genre.Id, rankingType);
        if (rankingList == null)
        {
            return new PagedResult<NovelInRankingDto>([], 0, request.PageSize, request.PageNumber);
        }

        // Get ranking entries with pagination
        var rankingEntries = await rankingRepository.GetRankingEntriesPaged(
            rankingList.Id, request.PageSize, request.PageNumber);

        // Map to NovelInRankingDto
        var novelDtos = mapper.Map<IEnumerable<NovelInRankingDto>>(rankingEntries);

        return new PagedResult<NovelInRankingDto>(
            novelDtos, rankingList.TotalNovels, request.PageSize, request.PageNumber);
    }
}
