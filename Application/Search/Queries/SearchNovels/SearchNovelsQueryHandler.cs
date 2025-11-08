using Application.Common;
using Application.Search.DTOs;
using Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Search.Queries.SearchNovels;

public class SearchNovelsQueryHandler(
    ILogger<SearchNovelsQueryHandler> logger,
    INovelSearchService searchService) : IRequestHandler<SearchNovelsQuery, PagedResult<NovelSearchResult>>
{
    public async Task<PagedResult<NovelSearchResult>> Handle(
        SearchNovelsQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Searching novels with query: {Query}, genres: {Genres}, status: {Status}, page: {Page}",
            request.Request.Query ?? "all",
            request.Request.Genres != null ? string.Join(", ", request.Request.Genres) : "all",
            request.Request.Status ?? "all",
            request.Request.PageNumber
        );

        return await searchService.SearchNovelsAsync(request.Request, cancellationToken);
    }
}
