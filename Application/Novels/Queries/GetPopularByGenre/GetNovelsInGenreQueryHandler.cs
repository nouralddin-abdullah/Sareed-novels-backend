using Application.Common;
using Application.Novels.DTOS;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetPopularByGenre;

internal class GetNovelsInGenreQueryHandler(ILogger<GetNovelsInGenreQueryHandler> logger, INovelGenresRepository novelGenresRepository, IMapper mapper) : IRequestHandler<GetNovelsInGenreQuery, PagedResult<NovelInRankingDto>>
{
    public async Task<PagedResult<NovelInRankingDto>> Handle(GetNovelsInGenreQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting popular by genere id {@request}", request);
        var (result, totalCount) = await novelGenresRepository.GetNovelsByGenre(request.Slug, request.PageSize, request.PageNumber, request.Sorting, request.IsCompleted);
        var resultDto = mapper.Map<IEnumerable<NovelInRankingDto>>(result);
        var response = new PagedResult<NovelInRankingDto>(resultDto, totalCount, request.PageSize, request.PageNumber);
        return response;
    }
}
