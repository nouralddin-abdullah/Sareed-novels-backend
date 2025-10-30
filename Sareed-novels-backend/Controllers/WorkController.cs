using Application.Chapters.Commands.ReorderChapter;
using Application.Chapters.Queries.GetChapterAuthor;
using Application.Chapters.Queries.GetChaptersAuthor;
using Application.Novels.Commands.ChangeCover;
using Application.Novels.Commands.CreateNovel;
using Application.Novels.Commands.DeleteWork;
using Application.Novels.Commands.DraftWork;
using Application.Novels.Commands.PublishWork;
using Application.Novels.Commands.UpdateNovel;
using Application.Novels.Queries.GetMyWorks;
using Application.Novels.Queries.GetWork;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers
{
    [ApiController]
    [Route("/api/myworks")]
    [Authorize]
    public class WorkController(IMediator mediator) : ControllerBase
    {

        [HttpPost]
        public async Task<IActionResult> CreateNovel(CreateNovelCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
        [HttpPatch("novel-cover/{novelId}")]
        public async Task<IActionResult> ChangeCover([FromRoute] Guid novelId, [FromForm] ChangeCoverCommandRequest request)
        {
            var command = new ChangerCoverCommand(novelId, request.CoverUrl);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
        [HttpPatch("{novelId}")]
        public async Task<IActionResult> UpdateNovel([FromRoute] Guid novelId, [FromBody] UpdateNovelCommandRequest request)
        {
            var command = new UpdateNovelCommand(novelId, request.Title, request.Summary, request.Status, request.GenreIds ?? null);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        [HttpPatch("{novelId}/draft")]
        public async Task<IActionResult> DraftWork([FromRoute] Guid novelId)
        {
            var command = new DraftWorkCommand(novelId);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        [HttpPatch("{novelId}/publish")]
        public async Task<IActionResult> PublishWork([FromRoute] Guid novelId)
        {
            var command = new PublishWorkCommand(novelId);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        [HttpDelete("{novelId}/delete")]
        public async Task<IActionResult> DeleteWork([FromRoute] Guid novelId)
        {
            var command = new DeleteWorkCommand(novelId);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetMyWorks([FromQuery] GetMyWorksQuery query)
        {
            var worksListPaged = await mediator.Send(query);
            return Ok(worksListPaged);
        }
        [HttpGet("{workId}")]
        public async Task<IActionResult> GetWork([FromRoute] Guid workId)
        {
            var query = new GetWorkQuery(workId);
            var workData = await mediator.Send(query);
            return Ok(workData);
        }
        [HttpGet("{workId}/chapters")]
        public async Task<IActionResult> GetWorkChapters([FromRoute] Guid workId)
        {
            var query = new GetChaptersAuthorQuery(workId);
            var workChaptersData = await mediator.Send(query);
            return Ok(workChaptersData);
        }

        [HttpPatch("{workId}/chapters")]
        public async Task<IActionResult> ReorderWorkChapters([FromRoute] Guid workId, ReorderChaptersRequest request)
        {
            var query = new ReorderChaptersCommand(workId, request.OrderedChapterIds);
            var result = await mediator.Send(query);
            if (result)
            {
                return Ok();
            }
            return BadRequest();
        }
        [HttpGet("{workId}/chapters/{chapterId}")]
        public async Task<IActionResult> ReorderWorkChapters([FromRoute] Guid workId, [FromRoute] Guid chapterId)
        {
            var query = new GetChapterAuthorQuery(workId, chapterId);
            var result = await mediator.Send(query);
            return Ok(result);
        }
    }
}
