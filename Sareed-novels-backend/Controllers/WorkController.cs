using Application.Novels.Commands.ChangeCover;
using Application.Novels.Commands.CreateNovel;
using Application.Novels.Commands.UpdateNovel;
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
            return Ok(result);
        }
        [HttpPatch("novel-cover/{novelId}")]
        public async Task<IActionResult> ChangeCover([FromRoute] Guid novelId, [FromForm] ChangeCoverCommandRequest request)
        {
            var command = new ChangerCoverCommand(novelId, request.CoverUrl);
            var result = await mediator.Send(command);
            return Ok(result);
        }
        [HttpPatch("{novelId}")]
        public async Task<IActionResult> UpdateNovel([FromRoute] Guid novelId, [FromBody] UpdateNovelCommandRequest request)
        {
            var command = new UpdateNovelCommand(novelId, request.Title, request.Summary, request.Status);
            var result = await mediator.Send(command);
            return Ok(result);
        }
    }
}
