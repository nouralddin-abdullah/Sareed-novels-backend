using Application.Comments.Commands.CreateComment;
using Application.Comments.Commands.DeleteComment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/comment")]
    public class CommentController(IMediator mediator) : ControllerBase
    {
        [HttpPost]
        [Route("/{chapterId}")]
        public async Task<IActionResult> CreateComment([FromRoute] Guid chapterId, [FromForm] CreateCommentRequest request)
        {
            var command = new CreateCommentCommand(chapterId, request.Content, request.AttachedImage, request.ParentCommentId);
            var result = await mediator.Send(command);
            if (result.Success)
            {
                return Created();
            }
            return BadRequest(result.Message);
        }
        [Route("/{commentId}")]
        [HttpDelete]
        public async Task<IActionResult> DeleteComment([FromRoute] Guid commentId)
        {
            var command = new DeleteCommentCommand(commentId);
            var result = await mediator.Send(command);
            if (result)
            {
                return NoContent();
            }
            return BadRequest();

        }
    }
}
