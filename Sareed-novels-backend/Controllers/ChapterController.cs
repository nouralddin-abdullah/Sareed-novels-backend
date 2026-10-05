using Application.Chapters.Commands.CreateChapter;
using Application.Chapters.Commands.DeleteChapter;
using Application.Chapters.Commands.TrackChapterView;
using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.Queries.GetChapterAuthor;
using Application.Chapters.Queries.GetChapterReader;
using Application.Chapters.Queries.GetChaptersAuthor;
using Application.Chapters.Queries.GetChaptersReader;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Application.Common;

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
            var command = new CreateChapterCommand(novelId, request.Status, request.Title, request.Content)
            {
                PublishAt = request.PublishAt
            };
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
            return BadRequest(new ApiError("OperationFailed", "تعذّر حذف الفصل. حاول مرة أخرى."));
        }
        /// <summary>
        /// Saves the author's chapter: its title and text (chapter format v1, #74), its status, or its status alone (#75:
        /// title and content left out together); a draft's schedule (#77: publishAt, a time to come or null to cancel)
        /// with any of these or alone. With baseRevision, the chapter's revision the editor's copy was loaded at, a
        /// title or text save from an older copy is refused, 409 ChapterChanged with the chapter's revision, and
        /// nothing is saved. Answers { success, message, revision }. With ?dryRun=true it checks and matches everything
        /// a save would, saves nothing, and answers what the save would delete:
        /// { paragraphsRemoved, commentsDeleted, removed: [{ paragraphId, commentsCount }] }.
        /// </summary>
        [HttpPatch("{chapterId}")]
        [Authorize]
        public async Task<IActionResult> UpdateChapter([FromRoute] Guid novelId, [FromRoute] Guid chapterId, UpdateChapterRequest request,
            [FromQuery] bool dryRun = false)
        {
            var command = new UpdateChapterCommand(chapterId, novelId, request.Title, request.Status, request.Content)
            {
                BaseRevision = request.BaseRevision,
                DryRun = dryRun,
                SetsSchedule = request.PublishAtSent,
                PublishAt = request.PublishAt
            };
            var result = await mediator.Send(command);
            if (dryRun)
            {
                return Ok(result.Preview);
            }
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
        /// <summary>
        /// A chapter in the reader; opening it counts a read (once per visitor per day). A download for offline reading
        /// says so with <c>?prefetch=true</c> (or <c>1</c>) or the header <c>X-Sard-Prefetch: 1</c>: the same answer,
        /// but no read is counted until the app sends POST .../view.
        /// </summary>
        [HttpGet("{chapterId}")]
        public async Task<IActionResult> ReorderWorkChapters([FromRoute] Guid novelId, [FromRoute] Guid chapterId, [FromQuery] string? prefetch)
        {
            var query = new GetChapterReaderQuery(novelId, chapterId)
            {
                TrackView = !IsYes(prefetch) && !IsYes(Request.Headers[PrefetchHeader].ToString())
            };
            var result = await mediator.Send(query);
            return Ok(result);
        }

        /// <summary>
        /// Counts one read of a chapter that was downloaded with prefetch and is now opened (offline reads are sent when
        /// the app is back online), with the reader's rules: the same visitor key (the signed-in user, or the device)
        /// and at most once a day. 204 whether or not this call counted (a repeat that day, the author, a chapter locked
        /// for the caller); 404 NovelNotFound/ChapterNotFound for a chapter readers can't open. Anonymous readers too.
        /// </summary>
        [HttpPost("{chapterId}/view")]
        public async Task<IActionResult> TrackChapterView([FromRoute] Guid novelId, [FromRoute] Guid chapterId)
        {
            await mediator.Send(new TrackChapterViewCommand(novelId, chapterId));
            return NoContent();
        }

        /// <summary>The header an app sends on a chapter download for offline reading (instead of ?prefetch=true).</summary>
        public const string PrefetchHeader = "X-Sard-Prefetch";

        private static bool IsYes(string? value) =>
            value?.Trim() is { } v && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
    }
}
