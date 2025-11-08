using Application.Search.Commands.MigrateNovels;
using Application.Search.DTOs;
using Application.Search.Queries.SearchNovels;
using Application.Search.Queries.SearchUsers;
using Application.Search.Queries.SuggestNovels;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController(IMediator mediator) : ControllerBase
{
    [HttpPost("novels")]
    public async Task<IActionResult> SearchNovels([FromBody] SearchNovelsRequest request)
    {
        var query = new SearchNovelsQuery(request);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("novels")]
    public async Task<IActionResult> SearchNovelsGet(
        [FromQuery] string? query,
        [FromQuery] List<string>? genres,
        [FromQuery] string? status,
        [FromQuery] List<ChapterCountRange>? chapterRanges,
        [FromQuery] ChapterCountRange? chapterRange,
        [FromQuery] NovelSortBy sortBy = NovelSortBy.Relevance,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var request = new SearchNovelsRequest
        {
            Query = query,
            Genres = genres,
            Status = status,
            ChapterRanges = chapterRanges,
            ChapterRange = chapterRange,
            SortBy = sortBy,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var searchQuery = new SearchNovelsQuery(request);
        var result = await mediator.Send(searchQuery);
        return Ok(result);
    }

    /// <summary>
    /// Get autocomplete suggestions as user types (returns max 5 results)
    /// </summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> SuggestNovels([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(new List<NovelSuggestion>());
        }

        var suggestQuery = new SuggestNovelsQuery(query, MaxSuggestions: 5);
        var suggestions = await mediator.Send(suggestQuery);
        return Ok(suggestions);
    }

    /// <summary>
    /// Search for users by username or display name
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string? query,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var request = new SearchUsersRequest
        {
            Query = query,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var searchQuery = new SearchUsersQuery(request);
        var result = await mediator.Send(searchQuery);
        return Ok(result);
    }

    [HttpPost("admin/reindex-novels")]
    public async Task<IActionResult> ReindexNovels()
    {
        var command = new MigrateNovelsToElasticsearchCommand();
        var result = await mediator.Send(command);

        if (result.Success)
        {
            return Ok(result);
        }
        return BadRequest(result);
    }
}
