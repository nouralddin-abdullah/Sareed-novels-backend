using Application.Common;
using Application.Novels.DTOS;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Rankings.Queries.GetGenreRanking;

public class GetGenreRankingQueryHandler(IRankingRepository rankingRepository, ILogger<GetGenreRankingQueryHandler> logger, IGenresRepository genresRepository, IMapper mapper) : IRequestHandler<GetGenreRankingQuery, PagedResult<NovelInRankingDto>>
{
    public async Task<PagedResult<NovelInRankingDto>> Handle(GetGenreRankingQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting {RankingType} ranking for genre {GenreSlug}",
            request.RankingType, request.GenreSlug);

        // Get genre by slug
        var genre = await genresRepository.GetBySlug(request.GenreSlug)
            ?? throw new NotFoundException($"Genre '{request.GenreSlug}' not found");

        // Get ranking list
        var rankingList = await rankingRepository.GetRankingListByGenreAndType(genre.Id, request.RankingType)
            ?? throw new NotFoundException($"No {request.RankingType} ranking found for genre {request.GenreSlug}. Rankings may not be calculated yet.");

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
