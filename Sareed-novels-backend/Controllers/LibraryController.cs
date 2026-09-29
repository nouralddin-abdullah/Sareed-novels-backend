using Application.Library.Commands.MigrateSequences;
using Application.Library.Commands.RecalculateSequences;
using Application.Library.Commands.RemoveFromLibrary;
using Application.Library.Commands.SetNewChapterNotifications;
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

    /// <summary>
    /// Removes the novel from the caller's library (#33): deletes her progress entry for it, so its new chapters stop
    /// notifying her. 204, also when it isn't in her library (the state is already as asked, #25). Reading the novel
    /// again adds it back, with notifications on.
    /// </summary>
    [HttpDelete("novel/{novelId}")]
    public async Task<IActionResult> RemoveFromLibrary([FromRoute] Guid novelId)
    {
        await mediator.Send(new RemoveFromLibraryCommand(novelId));
        return NoContent();
    }

    /// <summary>
    /// Turns one library novel's new-chapter notifications on or off for the caller (#33), body
    /// <c>{ "notifyNewChapters": bool }</c>: 204, also when it already was; 404 NotInLibrary when the novel isn't in her
    /// library; 400 ValidationFailed without the value.
    /// </summary>
    [HttpPatch("novel/{novelId}")]
    public async Task<IActionResult> SetNewChapterNotifications([FromRoute] Guid novelId, [FromBody] SetNewChapterNotificationsRequest request)
    {
        await mediator.Send(new SetNewChapterNotificationsCommand(novelId, request.NotifyNewChapters!.Value));
        return NoContent();
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
