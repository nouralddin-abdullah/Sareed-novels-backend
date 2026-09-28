using Application.Common;
using Application.Search.DTOs;
using Application.Services;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Search.Queries.SearchNovels;

public class SearchNovelsQueryHandler(
    ILogger<SearchNovelsQueryHandler> logger,
    INovelSearchService searchService,
    IGenresRepository genresRepository) : IRequestHandler<SearchNovelsQuery, PagedResult<NovelSearchResult>>
{
    public async Task<PagedResult<NovelSearchResult>> Handle(
        SearchNovelsQuery request,
        CancellationToken cancellationToken)
    {
        // A genre that doesn't exist is a 404, as the genre's own page answers (#25): an empty list read as "no novels
        // in this genre". Blank values are ignored; a genre is given by name or by slug, compared as the filter compares.
        var genres = request.Request.Genres?
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        request.Request.Genres = genres;
        if (genres is { Count: > 0 } && !await genresRepository.AreAllGenresAsync(genres, cancellationToken))
        {
            throw new NotFoundException("التصنيف غير موجود", "GenreNotFound");
        }

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
