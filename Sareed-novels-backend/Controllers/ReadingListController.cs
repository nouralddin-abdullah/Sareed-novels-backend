using Application.ReadingLists.Commands.AddNovelToList;
using Application.ReadingLists.Commands.CreateReadingList;
using Application.ReadingLists.Commands.DeleteReadingList;
using Application.ReadingLists.Commands.FollowReadingList;
using Application.ReadingLists.Commands.RemoveNovelFromList;
using Application.ReadingLists.Commands.UnfollowReadingList;
using Application.ReadingLists.Commands.UpdateReadingList;
using Application.ReadingLists.Queries.GetFollowedReadingLists;
using Application.ReadingLists.Queries.GetMyReadingLists;
using Application.ReadingLists.Queries.GetReadingListDetail;
using Application.ReadingLists.Queries.GetUserPublicReadingLists;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Authorize]
[Route("api/readinglist")]
public class ReadingListController(IMediator mediator) : ControllerBase
{
    [HttpGet("my-lists")]
    public async Task<IActionResult> GetMyReadingLists([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 12)
    {
        var query = new GetMyReadingListsQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("followed")]
    public async Task<IActionResult> GetFollowedReadingLists([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 12)
    {
        var query = new GetFollowedReadingListsQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("user/{userName}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUserPublicReadingLists([FromRoute] string userName, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 12)
    {
        var query = new GetUserPublicReadingListsQuery(userName)
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{readingListId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetReadingListDetail([FromRoute] Guid readingListId)
    {
        var query = new GetReadingListDetailQuery(readingListId);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateReadingList([FromForm] CreateReadingListCommand command)
    {
        var result = await mediator.Send(command);
        if (result.Success)
        {
            return Ok(result);
        }
        return BadRequest(result);
    }

    [HttpPatch("{readingListId}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateReadingList([FromRoute] Guid readingListId, [FromForm] UpdateReadingListRequest request)
    {
        var command = new UpdateReadingListCommand(
            readingListId,
            request.Name,
            request.Description,
            request.IsPublic,
            request.CoverImage
        );

        var result = await mediator.Send(command);
        if (result.Success)
        {
            return Ok(result);
        }
        return BadRequest(result);
    }

    [HttpDelete("{readingListId}")]
    public async Task<IActionResult> DeleteReadingList([FromRoute] Guid readingListId)
    {
        var command = new DeleteReadingListCommand(readingListId);
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return NoContent();
        }
        return BadRequest(result);
    }

    [HttpPost("{readingListId}/novels/{novelId}")]
    public async Task<IActionResult> AddNovelToReadingList([FromRoute] Guid readingListId, [FromRoute] Guid novelId)
    {
        var command = new AddNovelToListCommand
        {
            ReadingListId = readingListId,
            NovelId = novelId
        };

        var result = await mediator.Send(command);
        if (result.Success)
        {
            return Ok(result);
        }
        return BadRequest(result);
    }

    [HttpDelete("{readingListId}/novels/{novelId}")]
    public async Task<IActionResult> RemoveNovelFromReadingList([FromRoute] Guid readingListId, [FromRoute] Guid novelId)
    {
        var command = new RemoveNovelFromListCommand(readingListId, novelId);
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return NoContent();
        }
        return BadRequest(result);
    }

    [HttpPost("{readingListId}/follow")]
    public async Task<IActionResult> FollowReadingList([FromRoute] Guid readingListId)
    {
        var command = new FollowReadingListCommand(readingListId);
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return Ok(result);
        }
        return BadRequest(result);
    }

    [HttpDelete("{readingListId}/unfollow")]
    public async Task<IActionResult> UnfollowReadingList([FromRoute] Guid readingListId)
    {
        var command = new UnfollowReadingListCommand(readingListId);
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return Ok(result);
        }
        return BadRequest(result);
    }
}
