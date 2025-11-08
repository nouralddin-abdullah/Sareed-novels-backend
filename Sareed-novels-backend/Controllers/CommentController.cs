using Application.Comments.Commands.CreateComment;
using Application.Comments.Commands.DeleteComment;
using Application.Comments.Commands.LikeComment;
using Application.Comments.Commands.UnlikeComment;
using Application.Comments.Queries.GetChapterComments;
using Application.Comments.Queries.GetCommentReplies;
using Application.Comments.Queries.GetParagraphComments;
using Application.Comments.Queries.GetPostComments;
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
        [Route("chapter/{chapterId}")]
        public async Task<IActionResult> CreateChapterComment([FromRoute] Guid chapterId, [FromForm] CreateCommentRequest request)
        {
            var command = new CreateCommentCommand(chapterId, null, null, request.Content, request.AttachedImage, request.ParentCommentId);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result.Message);
            }
            return Ok(result);
        }
        
        [HttpPost]
        [Route("paragraph/{paragraphId}")]
        public async Task<IActionResult> CreateParagraphComment([FromRoute] Guid paragraphId, [FromForm] CreateCommentRequest request)
        {
            var command = new CreateCommentCommand(null, paragraphId, null, request.Content, request.AttachedImage, request.ParentCommentId);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result.Message);
            }
            return Ok(result);
        }

        [HttpPost]
        [Route("post/{postId}")]
        public async Task<IActionResult> CreatePostComment([FromRoute] Guid postId, [FromForm] CreateCommentRequest request)
        {
            var command = new CreateCommentCommand(null, null, postId, request.Content, request.AttachedImage, request.ParentCommentId);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result.Message);
            }
            return Ok(result);
        }
        
        [Route("{commentId}")]
        [HttpDelete]
        public async Task<IActionResult> DeleteComment([FromRoute] Guid commentId)
        {
            var command = new DeleteCommentCommand(commentId);
            var result = await mediator.Send(command);
            if (!result)
            {
                return BadRequest();
            }
            return NoContent();
        }
        
        [AllowAnonymous]
        [HttpGet]
        [Route("chapter/{chapterId}")]
        public async Task<IActionResult> GetChapterComments([FromRoute] Guid chapterId, [FromQuery] int? pageNumber, [FromQuery] int? pageSize, [FromQuery] string? sorting)
        {
            var query = new GetChapterCommentsQuery(chapterId, pageNumber ?? 1, pageSize ?? 10, sorting ?? "recent");
            var result = await mediator.Send(query);
            return Ok(result);
        }
        
        [AllowAnonymous]
        [HttpGet]
        [Route("paragraph/{paragraphId}")]
        public async Task<IActionResult> GetParagraphComments([FromRoute] Guid paragraphId, [FromQuery] int? pageNumber, [FromQuery] int? pageSize, [FromQuery] string? sorting)
        {
            var query = new GetParagraphCommentsQuery(paragraphId, pageNumber ?? 1, pageSize ?? 10, sorting ?? "recent");
            var result = await mediator.Send(query);
            return Ok(result);
        }
        
        [AllowAnonymous]
        [HttpGet]
        [Route("post/{postId}")]
        public async Task<IActionResult> GetPostComments([FromRoute] Guid postId, [FromQuery] int? pageNumber, [FromQuery] int? pageSize, [FromQuery] string? sorting)
        {
            var query = new GetPostCommentsQuery(postId, pageNumber ?? 1, pageSize ?? 10, sorting ?? "recent");
            var result = await mediator.Send(query);
            return Ok(result);
        }
        
        [AllowAnonymous]
        [HttpGet]
        [Route("chapter/comments/{parentCommentId}")]
        public async Task<IActionResult> GetCommentReplies([FromRoute] Guid parentCommentId, [FromQuery] int? pageNumber, [FromQuery] int? pageSize, [FromQuery] string? sorting)
        {
            var query = new GetCommentRepliesQuery(parentCommentId, pageNumber ?? 1, pageSize ?? 10, sorting ?? "oldest");
            var result = await mediator.Send(query);
            return Ok(result);
        }
        
        [HttpPost]
        [Route("{commentId}/like")]
        public async Task<IActionResult> LikeComment([FromRoute] Guid commentId)
        {
            var command = new LikeCommentCommand { CommentId = commentId };
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result.Message);
            }
            return Ok(result);
        }

        [HttpDelete]
        [Route("{commentId}/unlike")]
        public async Task<IActionResult> UnlikeComment([FromRoute] Guid commentId)
        {
            var command = new UnlikeCommentCommand { CommentId = commentId };
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result.Message);
            }
            return Ok(result);
        }
    }
}
