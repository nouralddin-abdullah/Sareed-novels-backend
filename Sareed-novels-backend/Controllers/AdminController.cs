using Application.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize] // TODO: Add admin role check
public class AdminController(IMediator mediator) : ControllerBase
{
    [HttpPost("elasticsearch/init")]
    public async Task<IActionResult> InitializeElasticsearchIndexes(
        [FromServices] IEntitySearchService entitySearchService,
        [FromServices] INovelSearchService novelSearchService,
        [FromServices] IUserSearchService userSearchService)
    {
        var results = new
        {
            EntityIndex = await entitySearchService.EnsureIndexExistsAsync(),
            NovelIndex = await novelSearchService.EnsureIndexExistsAsync(),
            UserIndex = await userSearchService.EnsureIndexExistsAsync()
        };

        return Ok(new
        {
            success = results.EntityIndex && results.NovelIndex && results.UserIndex,
            message = "Elasticsearch indexes initialization attempted",
            details = results
        });
    }

    [HttpPost("elasticsearch/reindex-entities/{novelId}")]
    public async Task<IActionResult> ReindexNovelEntities(
        [FromRoute] Guid novelId,
        [FromServices] IEntitySearchService entitySearchService)
    {
        var count = await entitySearchService.ReindexNovelEntitiesAsync(novelId);
        
        return Ok(new
        {
            success = count > 0,
            message = $"Reindexed {count} entities for novel {novelId}",
            count
        });
    }

    [HttpPost("elasticsearch/reindex-novels")]
    public async Task<IActionResult> ReindexNovels(
        [FromServices] INovelSearchService novelSearchService)
    {
        var count = await novelSearchService.ReindexAllNovelsAsync();
        
        return Ok(new
        {
            success = count > 0,
            message = $"Reindexed {count} novels",
            count
        });
    }

    [HttpPost("elasticsearch/reindex-users")]
    public async Task<IActionResult> ReindexUsers(
        [FromServices] IUserSearchService userSearchService)
    {
        var count = await userSearchService.ReindexAllUsersAsync();
        
        return Ok(new
        {
            success = count > 0,
            message = $"Reindexed {count} users",
            count
        });
    }
}
