using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Search.Queries.SuggestNovels;

public class SuggestNovelsQueryHandler(
    ILogger<SuggestNovelsQueryHandler> logger,
    Application.Services.INovelSearchService searchService) : IRequestHandler<SuggestNovelsQuery, List<NovelSuggestion>>
{
    public async Task<List<NovelSuggestion>> Handle(SuggestNovelsQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length < 2)
        {
            return new List<NovelSuggestion>();
        }

        logger.LogDebug("Generating suggestions for query: {Query}", request.Query);

        // Use the existing search service but limit results and only get titles
        var searchRequest = new Application.Search.DTOs.SearchNovelsRequest
        {
            Query = request.Query,
            PageNumber = 1,
            PageSize = request.MaxSuggestions,
            SortBy = Application.Search.DTOs.NovelSortBy.Relevance
        };

        var results = await searchService.SearchNovelsAsync(searchRequest, cancellationToken);

        return results.Items.Select(r => new NovelSuggestion
        {
            Id = r.Id,
            Title = r.Title,
            Slug = r.Slug,
            CoverImageUrl = r.CoverImageUrl
        }).ToList();
    }
}
