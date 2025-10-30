using Application.Chapters.Commands.CreateChapter;
using Application.Chapters.Commands.DeleteChapter;
using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.Queries.GetChapterAuthor;
using Application.Chapters.Queries.GetChapterReader;
using Application.Chapters.Queries.GetChaptersAuthor;
using Application.Chapters.Queries.GetChaptersReader;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers
{
    [ApiController]
    [Route("api/novel/{novelId}/chapter")]
    public class ChapterController(IMediator mediator) : ControllerBase
    {
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateChapter([FromRoute] Guid novelId, CreateChapterRequest request)
        {
            var command = new CreateChapterCommand(novelId, request.Status, request.Title, request.Content);
            var result = await mediator.Send(command);
            return Ok(result);
        }
        [HttpDelete("{chapterId}")]
        [Authorize]
        public async Task<IActionResult> DeleteChapter([FromRoute] Guid novelId, [FromRoute] Guid chapterId)
        {
            var command = new DeleteChapterCommand(novelId, chapterId);
            var result = await mediator.Send(command);
            if (result)
            {
                return NoContent();
            }
            return BadRequest();
        }
        [HttpPatch("{chapterId}")]
        [Authorize]
        public async Task<IActionResult> UpdateChapter([FromRoute] Guid novelId, [FromRoute] Guid chapterId, UpdateChapterRequest request)
        {
            var command = new UpdateChapterCommand(chapterId, novelId, request.Title, request.Status, request.Content);
            var result = await mediator.Send(command);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
        [HttpGet]
        public async Task<IActionResult> GetNovelChapters([FromRoute] Guid novelId)
        {
            var query = new GetChaptersReaderQuery(novelId);
            var NovelChaptersData = await mediator.Send(query);
            return Ok(NovelChaptersData);
        }
        [HttpGet("{chapterId}")]
        public async Task<IActionResult> ReorderWorkChapters([FromRoute] Guid novelId, [FromRoute] Guid chapterId)
        {
            var query = new GetChapterReaderQuery(novelId, chapterId);
            var result = await mediator.Send(query);
            return Ok(result);
        }
    }
}
