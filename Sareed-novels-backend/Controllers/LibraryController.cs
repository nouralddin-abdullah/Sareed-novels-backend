using Application.Library.Commands.MigrateSequences;
using Application.Library.Commands.RecalculateSequences;
using Application.Library.Commands.TrackProgress;
using Application.Library.Queries.GetMyLibrary;
using Application.Library.Queries.GetNovelProgress;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Authorize]
[Route("api/library")]
public class LibraryController(IMediator mediator) : ControllerBase
{
    [HttpGet("reading-progress")]
    public async Task<IActionResult> GetMyLibrary([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
    {
        var query = new GetMyLibraryQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("novel/{novelId}/progress")]
    [AllowAnonymous]
    public async Task<IActionResult> GetNovelProgress([FromRoute] Guid novelId)
    {
        var query = new GetNovelProgressQuery(novelId);
        var result = await mediator.Send(query);

        if (result == null)
        {
            return Ok(new { hasProgress = false });
        }

        return Ok(new { hasProgress = true, progress = result });
    }

    [HttpPost("track-progress/{chapterId}")]
    public async Task<IActionResult> TrackReadingProgress([FromRoute] Guid chapterId)
    {
        var command = new TrackReadingProgressCommand(chapterId);
        var result = await mediator.Send(command);

        if (result.Success)
        {
            return Ok(result);
        }
        return BadRequest(result);
    }

    [HttpPost("novel/{novelId}/recalculate-sequences")]
    public async Task<IActionResult> RecalculateSequences([FromRoute] Guid novelId)
    {
        var command = new RecalculateChapterSequencesCommand(novelId);
        var result = await mediator.Send(command);

        if (result.Success)
        {
            return Ok(result);
        }
        return BadRequest(result);
    }

    [HttpPost("admin/migrate-sequences")]
    [Authorize(Roles = "Admin")] // You may need to adjust this based on your auth setup
    public async Task<IActionResult> MigrateSequences()
    {
        var command = new MigratePublishedSequencesCommand();
        var result = await mediator.Send(command);

        if (result.Success)
        {
            return Ok(result);
        }
        return BadRequest(result);
    }
}
