using Application.Common;
using Application.Search.DTOs;

namespace Application.Services;

/// <summary>Novel search, served straight from SQL (normalized Novel.SearchTitle); there is no index to maintain.</summary>
public interface INovelSearchService
{
    Task<PagedResult<NovelSearchResult>> SearchNovelsAsync(
        SearchNovelsRequest request,
        CancellationToken cancellationToken = default);
}
